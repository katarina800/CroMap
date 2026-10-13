// Repositories/PasswordResetRepository.cs
using System.Security.Cryptography;
using System.Text;
using CroMap.Data;
using Dapper;

namespace CroMap.Repositories
{
    public class PasswordResetRepository
    {
        private readonly DatabaseConnection _db;
        private readonly byte[] _hmacKey;

        /// <summary>Najviše ovoliko pogrešnih pokušaja po kodu prije nego što se poništi.</summary>
        private const int MaxAttempts = 5;

        /// <summary>Najmanji razmak između dva slanja koda istom korisniku.</summary>
        private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

        public PasswordResetRepository(DatabaseConnection db, IConfiguration config)
        {
            _db = db;
            // Kod u bazi nije u čistom obliku: HMAC s tajnom poslužitelja. Tko
            // dobije kopiju baze ne može iz nje pročitati žive kodove, a samih
            // 900.000 mogućih kodova ne može unaprijed izračunati bez ključa.
            _hmacKey = Encoding.UTF8.GetBytes(config["Jwt:Key"]
                ?? throw new InvalidOperationException("Jwt:Key nije postavljen."));
        }

        private string HashCode(int userId, string code)
        {
            using var hmac = new HMACSHA256(_hmacKey);
            // userId ulazi u hash pa isti kod kod dva korisnika daje različit zapis.
            var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{userId}:{code}"));
            return Convert.ToHexString(bytes);
        }

        /// <summary>
        /// Sprema novi kod (zamjenjuje stari). Vraća false ako je kod za ovog
        /// korisnika već poslan unutar <see cref="ResendCooldown"/> — tada se
        /// ništa ne mijenja i e-mail se ne šalje (zaštita od zatrpavanja
        /// tuđeg sandučića zahtjevima za reset).
        /// </summary>
        public async Task<bool> CreateResetTokenAsync(int userId, string code)
        {
            using var conn = _db.CreateConnection();
            var now = DateTime.UtcNow;

            var lastCreated = await conn.QueryFirstOrDefaultAsync<DateTime?>(
                "SELECT created_at FROM password_reset_tokens WHERE user_id = @UserId",
                new { UserId = userId });
            if (lastCreated.HasValue && now - lastCreated.Value < ResendCooldown)
                return false;

            // Upsert (jedinstveni indeks na user_id): dva istovremena zahtjeva za
            // istog korisnika ne smiju završiti greškom prije slanja e-maila.
            await conn.ExecuteAsync(@"
                INSERT INTO password_reset_tokens (user_id, token, expires_at, created_at, attempts)
                VALUES (@UserId, @Token, @ExpiresAt, @CreatedAt, 0)
                ON CONFLICT (user_id) DO UPDATE
                SET token = EXCLUDED.token, expires_at = EXCLUDED.expires_at,
                    created_at = EXCLUDED.created_at, attempts = 0",
                new
                {
                    UserId = userId,
                    Token = HashCode(userId, code),
                    ExpiresAt = now.AddHours(1),
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
        public async Task<(int UserId, bool IsValid)> ValidateTokenAsync(string code, string email)
        {
            using var conn = _db.CreateConnection();

            var row = await conn.QueryFirstOrDefaultAsync<ResetRow>(@"
                UPDATE password_reset_tokens t
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

            var expected = HashCode(row.UserId, code);
            var matches = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(row.Token), Encoding.UTF8.GetBytes(expected));

            return matches ? (row.UserId, true) : (0, false);
        }

        private class ResetRow
        {
            public int UserId { get; set; }
            public string Token { get; set; } = "";
        }

        /// <summary>Briše kod korisnika (nakon uspješne promjene lozinke ili neuspjelog slanja).</summary>
        public async Task DeleteTokensForUserAsync(int userId)
        {
            using var conn = _db.CreateConnection();
            await conn.ExecuteAsync(
                "DELETE FROM password_reset_tokens WHERE user_id = @UserId",
                new { UserId = userId });
        }
    }
}
