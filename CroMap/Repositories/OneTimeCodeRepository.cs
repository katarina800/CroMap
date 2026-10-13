// Repositories/OneTimeCodeRepository.cs
using System.Security.Cryptography;
using System.Text;
using CroMap.Data;
using Dapper;

namespace CroMap.Repositories
{
    /// <summary>
    /// Zajednička logika za jednokratne šesteroznamenkaste kodove poslane e-mailom
    /// (reset lozinke, potvrda e-mail adrese). Svaka tablica ima isti oblik:
    /// user_id (jedinstven), token (HMAC hash), expires_at, created_at, attempts.
    /// </summary>
    public abstract class OneTimeCodeRepository
    {
        protected readonly DatabaseConnection Db;
        private readonly byte[] _hmacKey;

        /// <summary>Najviše ovoliko pogrešnih pokušaja po kodu prije nego što se poništi.</summary>
        protected const int MaxAttempts = 5;

        /// <summary>Najmanji razmak između dva slanja koda istom korisniku.</summary>
        private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

        /// <summary>Koliko kod vrijedi.</summary>
        protected abstract TimeSpan Lifetime { get; }

        /// <summary>
        /// Ime tablice. Smije biti SAMO konstanta iz koda (umeće se u SQL), nikad
        /// vrijednost koja dolazi od klijenta.
        /// </summary>
        protected abstract string Table { get; }

        /// <summary>Dodatni SET dio upserta (počinje zarezom), npr. za poništavanje dozvole.</summary>
        protected virtual string ExtraUpsertSet => "";

        protected OneTimeCodeRepository(DatabaseConnection db, IConfiguration config)
        {
            Db = db;
            // Kod u bazi nije u čistom obliku: HMAC s tajnom poslužitelja. Tko
            // dobije kopiju baze ne može iz nje pročitati žive kodove, a samih
            // 900.000 mogućih kodova ne može unaprijed izračunati bez ključa.
            _hmacKey = Encoding.UTF8.GetBytes(config["Jwt:Key"]
                ?? throw new InvalidOperationException("Jwt:Key nije postavljen."));
        }

        /// <summary>
        /// HMAC nad (tablica, korisnik, vrijednost): isti kod kod dva korisnika
        /// daje različit zapis, a kod za potvrdu e-maila ne može poslužiti za reset.
        /// </summary>
        protected string Hash(int userId, string value)
        {
            using var hmac = new HMACSHA256(_hmacKey);
            return Convert.ToHexString(
                hmac.ComputeHash(Encoding.UTF8.GetBytes($"{Table}:{userId}:{value}")));
        }

        protected static bool ConstantTimeEquals(string a, string b) =>
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

        /// <summary>
        /// Sprema novi kod (zamjenjuje stari). Vraća false ako je kod za ovog
        /// korisnika već poslan unutar razmaka od 60 s — tada se ništa ne mijenja
        /// i e-mail se ne šalje (zaštita od zatrpavanja tuđeg sandučića).
        /// </summary>
        public async Task<bool> CreateAsync(int userId, string code)
        {
            using var conn = Db.CreateConnection();
            var now = DateTime.UtcNow;

            var lastCreated = await conn.QueryFirstOrDefaultAsync<DateTime?>(
                $"SELECT created_at FROM {Table} WHERE user_id = @UserId",
                new { UserId = userId });
            if (lastCreated.HasValue && now - lastCreated.Value < ResendCooldown)
                return false;

            // Upsert (jedinstveni indeks na user_id): dva istovremena zahtjeva za
            // istog korisnika ne smiju završiti greškom prije slanja e-maila.
            await conn.ExecuteAsync($@"
                INSERT INTO {Table} (user_id, token, expires_at, created_at, attempts)
                VALUES (@UserId, @Token, @ExpiresAt, @CreatedAt, 0)
                ON CONFLICT (user_id) DO UPDATE
                SET token = EXCLUDED.token, expires_at = EXCLUDED.expires_at,
                    created_at = EXCLUDED.created_at, attempts = 0{ExtraUpsertSet}",
                new
                {
                    UserId = userId,
                    Token = Hash(userId, code),
                    ExpiresAt = now.Add(Lifetime),
                    CreatedAt = now
                });
            return true;
        }

        /// <summary>
        /// Provjerava kod uz e-mail: kod mora pripadati baš tom korisniku.
        ///
        /// Pokušaj se broji PRIJE usporedbe, u istoj atomarnoj naredbi koja
        /// dohvaća zapis — pa paralelni zahtjevi ne mogu premašiti
        /// <see cref="MaxAttempts"/> pogađanja. Istekao ili iscrpljen kod se
        /// ne vraća uopće.
        /// </summary>
        public async Task<(int UserId, bool IsValid)> ValidateAsync(string code, string email)
        {
            using var conn = Db.CreateConnection();

            var row = await conn.QueryFirstOrDefaultAsync<CodeRow>($@"
                UPDATE {Table} t
                SET attempts = t.attempts + 1
                FROM users u
                WHERE u.id = t.user_id
                  AND LOWER(u.email) = LOWER(@Email)
                  AND t.attempts < @MaxAttempts
                  AND t.expires_at > @Now
                RETURNING t.user_id AS UserId, t.token AS Token",
                new { Email = email, MaxAttempts, Now = DateTime.UtcNow });

            if (row == null)
                return (0, false);

            return ConstantTimeEquals(row.Token, Hash(row.UserId, code))
                ? (row.UserId, true)
                : (0, false);
        }

        /// <summary>Briše kod korisnika (nakon uspjeha ili neuspjelog slanja).</summary>
        public async Task DeleteForUserAsync(int userId)
        {
            using var conn = Db.CreateConnection();
            await conn.ExecuteAsync(
                $"DELETE FROM {Table} WHERE user_id = @UserId",
                new { UserId = userId });
        }

        public class CodeRow
        {
            public int UserId { get; set; }
            public string Token { get; set; } = "";
        }
    }
}
