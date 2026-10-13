// Repositories/EmailVerificationRepository.cs
using CroMap.Data;

namespace CroMap.Repositories
{
    /// <summary>
    /// Kodovi kojima korisnik pri registraciji dokazuje da mu e-mail adresa
    /// pripada (kod stiže samo na tu adresu).
    /// </summary>
    public class EmailVerificationRepository : OneTimeCodeRepository
    {
        protected override string Table => "email_verification_codes";
        protected override TimeSpan Lifetime => TimeSpan.FromHours(24);

        public EmailVerificationRepository(DatabaseConnection db, IConfiguration config) : base(db, config) { }
    }
}
