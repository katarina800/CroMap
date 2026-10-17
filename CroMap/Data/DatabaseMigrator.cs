using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace CroMap.Data;

/// <summary>
/// Primjenjuje SQL migracije iz mape <c>Migrations/</c> i pamti koje su već primijenjene.
/// </summary>
/// <remarks>
/// <para>
/// Dosad se svaka migracija pokretala ručno, lijepljenjem u pgAdmin, a nigdje nije
/// pisalo što je produkcija stvarno vidjela. To se jednom već skupo naplatilo:
/// tablica <c>notifications</c> postojala je od prije u starijem obliku, pa je
/// <c>CREATE TABLE IF NOT EXISTS</c> u migraciji obavijesti tiho preskočen —
/// "IF NOT EXISTS" gleda samo ime tablice, ne i stupce. Aplikacija se digla bez
/// greške, a svaki upis obavijesti padao je na <c>column "actor_user_id" does not
/// exist</c>. Otkrilo se tek kad je korisnik prijavio da obavijesti ne stižu.
/// </para>
/// <para>
/// Migracije se izvode redom po imenu datoteke (zato brojčani prefiks), svaka u
/// vlastitoj transakciji. Ako jedna padne, poništi se i podiže se iznimka — bolje
/// da se aplikacija ne digne nego da radi nad polovično migriranom bazom.
/// </para>
/// </remarks>
public sealed class DatabaseMigrator
{
    private readonly string _connectionString;
    private readonly string _migrationsPath;
    private readonly ILogger<DatabaseMigrator> _logger;

    /// <summary>
    /// Ključ PostgreSQL savjetodavne brave. Render zna držati više instanci
    /// aplikacije; bez brave bi se pri istovremenom deployu dvije instance
    /// natjecale oko istih migracija.
    /// </summary>
    private const long LockKey = 8891_2026;

    public DatabaseMigrator(
        string connectionString,
        string migrationsPath,
        ILogger<DatabaseMigrator> logger)
    {
        _connectionString = connectionString;
        _migrationsPath = migrationsPath;
        _logger = logger;
    }

    public async Task MigrateAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_migrationsPath))
        {
            _logger.LogWarning("Mapa s migracijama ne postoji: {Path}", _migrationsPath);
            return;
        }

        var files = Directory
            .GetFiles(_migrationsPath, "*.sql")
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToList();

        if (files.Count == 0)
        {
            _logger.LogWarning("Nema .sql datoteka u {Path}", _migrationsPath);
            return;
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Brava se drži do kraja veze; druga instanca ovdje čeka umjesto da
        // pokreće iste migracije usporedno.
        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_lock(@k)", conn))
        {
            lockCmd.Parameters.AddWithValue("k", LockKey);
            await lockCmd.ExecuteNonQueryAsync(ct);
        }

        try
        {
            await EnsureHistoryTableAsync(conn, ct);

            var applied = await LoadAppliedAsync(conn, ct);

            // Prvi put nad bazom koja već postoji: migracije su ručno
            // primijenjene i ne smiju se ponoviti. Jedna od njih radi
            // "UPDATE videos" nad postojećim redcima — ponovno pokretanje
            // ne bi bilo bezopasno. Zato se zatečeno stanje samo zabilježi
            // kao polazište, a prati se sve što dolazi poslije.
            if (applied.Count == 0 && await DatabaseAlreadyBuiltAsync(conn, ct))
            {
                _logger.LogWarning(
                    "Baza već postoji, a povijest migracija je prazna — "
                        + "zatečenih {Count} migracija upisuje se kao polazište, bez izvođenja.",
                    files.Count);

                foreach (var file in files)
                {
                    var sqlText = await File.ReadAllTextAsync(file, ct);
                    await RecordAsync(conn, Path.GetFileName(file), sqlText, baseline: true, ct);
                }

                return;
            }

            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                var sql = await File.ReadAllTextAsync(file, ct);

                if (applied.TryGetValue(name, out var recordedChecksum))
                {
                    var current = Checksum(sql);
                    if (!string.Equals(recordedChecksum, current, StringComparison.Ordinal))
                    {
                        // Već primijenjena migracija je izmijenjena. Baza i
                        // repozitorij se više ne poklapaju, a tiho preskakanje
                        // bi tu razliku zakopalo.
                        throw new InvalidOperationException(
                            $"Migracija '{name}' je izmijenjena nakon što je primijenjena. "
                                + "Napravi novu migraciju umjesto da mijenjaš staru.");
                    }

                    continue;
                }

                _logger.LogInformation("Primjenjujem migraciju {Name}", name);

                await using var tx = await conn.BeginTransactionAsync(ct);
                try
                {
                    await using (var cmd = new NpgsqlCommand(sql, conn, tx))
                    {
                        cmd.CommandTimeout = 300;
                        await cmd.ExecuteNonQueryAsync(ct);
                    }

                    await RecordAsync(conn, name, sql, baseline: false, ct, tx);
                    await tx.CommitAsync(ct);
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync(ct);
                    _logger.LogError(ex, "Migracija {Name} nije uspjela — poništena.", name);
                    throw;
                }

                _logger.LogInformation("Migracija {Name} primijenjena.", name);
            }
        }
        finally
        {
            await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(@k)", conn);
            unlock.Parameters.AddWithValue("k", LockKey);
            await unlock.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task EnsureHistoryTableAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                naziv            TEXT PRIMARY KEY,
                kontrolni_zbroj  TEXT        NOT NULL,
                polaziste        BOOLEAN     NOT NULL DEFAULT FALSE,
                primijenjeno     TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<Dictionary<string, string>> LoadAppliedAsync(
        NpgsqlConnection conn,
        CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        await using var cmd = new NpgsqlCommand(
            "SELECT naziv, kontrolni_zbroj FROM schema_migrations", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
            result[reader.GetString(0)] = reader.GetString(1);

        return result;
    }

    /// <summary>Postoji li tablica <c>users</c> — znak da baza nije prazna.</summary>
    private static async Task<bool> DatabaseAlreadyBuiltAsync(
        NpgsqlConnection conn,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT to_regclass('public.users') IS NOT NULL", conn);
        return await cmd.ExecuteScalarAsync(ct) is true;
    }

    private static async Task RecordAsync(
        NpgsqlConnection conn,
        string name,
        string sql,
        bool baseline,
        CancellationToken ct,
        NpgsqlTransaction? tx = null)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO schema_migrations (naziv, kontrolni_zbroj, polaziste)
            VALUES (@naziv, @zbroj, @polaziste)
            ON CONFLICT (naziv) DO NOTHING
            """,
            conn,
            tx);

        cmd.Parameters.AddWithValue("naziv", name);
        cmd.Parameters.AddWithValue("zbroj", Checksum(sql));
        cmd.Parameters.AddWithValue("polaziste", baseline);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// SHA-256 sadržaja, neosjetljiv na razliku CRLF/LF — inače bi ista
    /// migracija imala različit zbroj ovisno o tome tko ju je zadnji spremio.
    /// </summary>
    private static string Checksum(string sql)
    {
        var normalized = sql.Replace("\r\n", "\n");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes);
    }
}
