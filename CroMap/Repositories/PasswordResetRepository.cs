// Repositories/PasswordResetRepository.cs
using System.Security.Cryptography;
using CroMap.Data;
using Dapper;

namespace CroMap.Repositories
{
    /// <summary>
    /// Kodovi za reset lozinke. Tijek: kod (6 znamenki, e-mailom) → provjera koda
    /// na poslužitelju → jednokratna dozvola (<c>resetToken</c>, 256 bita) →
    /// promjena lozinke samo uz tu dozvolu.
    /// </summary>
    public class PasswordResetRepository : OneTimeCodeRepository
    {
        private static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(10);

        protected override string Table => "password_reset_tokens";
        protected override TimeSpan Lifetime => TimeSpan.FromHours(1);

        // Novi kod poništava već izdanu dozvolu — inače bi stara i dalje vrijedila
        // iako je korisnik u međuvremenu zatražio reset ispočetka.
        protected override string ExtraUpsertSet => ", ticket = NULL, ticket_expires_at = NULL";

        public PasswordResetRepository(DatabaseConnection db, IConfiguration config) : base(db, config) { }

        /// <summary>
        /// Nakon što je 6-znamenkasti kod ispravno unesen: poništava ga (više se ne
        /// može iskoristiti) i izdaje dozvolu za promjenu lozinke. Dozvola je
        /// nasumičan 256-bitni niz — za razliku od koda ne da se pogoditi, pa
        /// završni korak više ne ovisi o šest znamenki.
        /// </summary>
        public async Task<string> IssueTicketAsync(int userId)
        {
            var ticket = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');

            using var conn = Db.CreateConnection();
            // token = '' : prazan niz nikad ne odgovara hashu, pa se stari kod
            // ne može ponovno upotrijebiti.
            await conn.ExecuteAsync(@"
                UPDATE password_reset_tokens
                SET token = '', ticket = @Ticket, ticket_expires_at = @Expires
                WHERE user_id = @UserId",
                new
                {
                    UserId = userId,
                    Ticket = Hash(userId, "ticket:" + ticket),
                    Expires = DateTime.UtcNow.Add(TicketLifetime)
                });
            return ticket;
        }

        /// <summary>Vrijedi li dozvola za korisnika s ovim e-mailom. Ne troši je.</summary>
        public async Task<(int UserId, bool IsValid)> RedeemTicketAsync(string ticket, string email)
        {
            using var conn = Db.CreateConnection();
            var row = await conn.QueryFirstOrDefaultAsync<TicketRow>(@"
                SELECT t.user_id AS UserId, t.ticket AS Ticket
                FROM password_reset_tokens t
                JOIN users u ON u.id = t.user_id
                WHERE LOWER(u.email) = LOWER(@Email)
                  AND t.ticket IS NOT NULL
                  AND t.ticket_expires_at > @Now",
                new { Email = email, Now = DateTime.UtcNow });

            if (row == null)
                return (0, false);

            return ConstantTimeEquals(row.Ticket, Hash(row.UserId, "ticket:" + ticket))
                ? (row.UserId, true)
                : (0, false);
        }

        public class TicketRow
        {
            public int UserId { get; set; }
            public string Ticket { get; set; } = "";
        }
    }
}
