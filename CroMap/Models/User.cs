namespace CroMap.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string? PasswordHash { get; set; }
        public DateOnly? BirthDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsAdmin { get; set; }
        public string? GoogleId { get; set; }

        public string Language { get; set; } = "hr";
        public string AuthProvider { get; set; } = "local";

        /// <summary>Je li korisnik potvrdio da mu e-mail adresa pripada (kod poslan e-mailom).</summary>
        public bool EmailVerified { get; set; }
    }
}