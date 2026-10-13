using CroMap.Models;
using CroMap.Data;
using Dapper;
using System.Data;

namespace CroMap.Repositories
{
    public class UserRepository
    {
        private readonly DatabaseConnection _dbConnection;
        private readonly ILogger<UserRepository> _logger;

        public UserRepository(DatabaseConnection dbConnection, ILogger<UserRepository> logger)
        {
            _dbConnection = dbConnection;
            _logger = logger;
        }

        public async Task<int> RegisterAsync(User user)
        {
            using var connection = _dbConnection.CreateConnection();

            var hashedPassword = string.IsNullOrEmpty(user.PasswordHash)
                ? null
                : BCrypt.Net.BCrypt.HashPassword(user.PasswordHash);

            var sql = @"
        INSERT INTO users (username, first_name, last_name, email, phone, password_hash, birth_date, language, created_at)
        VALUES (@Username, @FirstName, @LastName, @Email, @Phone, @PasswordHash, @BirthDate, @Language, @CreatedAt)
        RETURNING id";

            var newId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                user.Username,
                user.FirstName,
                user.LastName,
                user.Email,
                user.Phone,
                PasswordHash = hashedPassword,
                BirthDate = user.BirthDate.HasValue
                    ? user.BirthDate.Value.ToDateTime(TimeOnly.MinValue)
                    : (DateTime?)null,
                Language = user.Language ?? "hr",   // 👈 DODANO
                user.CreatedAt
            });

            _logger.LogInformation($"✅ User registered: {user.Username}");
            return newId;
        }

        // 🔥 DODAJ OVU METODU ZA LOGIN PO USERNAME
        // 🔥 METODA ZA LOGIN PO USERNAME (ispravljena)
        public async Task<User> LoginByUsernameAsync(string username, string password)
        {
            using var connection = _dbConnection.CreateConnection();

            _logger.LogInformation($"🔐 Login attempt - Username: '{username}'");

            // 🔥 ISPRAVLJEN SQL - dodan is_admin
            var sql = @"
        SELECT 
            id, 
            username, 
            first_name AS FirstName, 
            last_name AS LastName, 
            email, 
            phone, 
            password_hash AS PasswordHash, 
            birth_date AS BirthDate, 
            created_at AS CreatedAt,
            is_admin AS IsAdmin,
            email_verified AS EmailVerified
        FROM users 
        WHERE LOWER(username) = LOWER(@Username)";

            var user = await connection.QueryFirstOrDefaultAsync<User>(sql, new { Username = username });

            if (user == null)
            {
                _logger.LogWarning($"❌ User not found with username: '{username}'");
                return null;
            }

            _logger.LogInformation($"✅ User found: {user.Username} (IsAdmin: {user.IsAdmin})");

            if (string.IsNullOrEmpty(user.PasswordHash))
            {
                _logger.LogWarning($"❌ User {username} has no password (Google-only account)");
                return null;
            }

            bool passwordValid = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);

            _logger.LogInformation($"Password verification result: {passwordValid}");

            if (passwordValid)
            {
                _logger.LogInformation($"✅ Login successful for: {user.Username}");
                return user;
            }

            _logger.LogWarning($"❌ Invalid password for: {user.Username}");
            return null;
        }

        public async Task<IEnumerable<User>> GetAllUsersAsync()
        {
            using var connection = _dbConnection.CreateConnection();

            var sql = @"
        SELECT 
            id, 
            username, 
            first_name AS FirstName, 
            last_name AS LastName, 
            email, 
            phone, 
            password_hash AS PasswordHash, 
            birth_date AS BirthDate, 
            created_at AS CreatedAt,
            is_admin AS IsAdmin
        FROM users 
        ORDER BY id";

            return await connection.QueryAsync<User>(sql);
        }

        public async Task<bool> UsernameExistsAsync(string username)
        {
            using var connection = _dbConnection.CreateConnection();
            var count = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM users WHERE LOWER(username) = LOWER(@Username)",
                new { Username = username });
            return count > 0;
        }

        /// <summary>
        /// Briše lokalni račun s ovom adresom koji još nije potvrđen. Potvrđeni,
        /// Google i administratorski računi se nikad ne diraju.
        /// </summary>
        public async Task<int> DeleteUnverifiedByEmailAsync(string email)
        {
            using var connection = _dbConnection.CreateConnection();
            return await connection.ExecuteAsync(
                @"DELETE FROM users
                  WHERE LOWER(email) = LOWER(@Email)
                    AND email_verified = FALSE
                    AND google_id IS NULL
                    AND is_admin = FALSE",
                new { Email = email });
        }

        /// <summary>
        /// Briše lokalni račun s ovim korisničkim imenom koji je neprovjeren i stariji
        /// od 10 minuta (korisnik je vjerojatno pogriješio u e-mail adresi).
        /// </summary>
        public async Task<int> DeleteStaleUnverifiedByUsernameAsync(string username)
        {
            using var connection = _dbConnection.CreateConnection();
            return await connection.ExecuteAsync(
                @"DELETE FROM users
                  WHERE LOWER(username) = LOWER(@Username)
                    AND email_verified = FALSE
                    AND google_id IS NULL
                    AND is_admin = FALSE
                    AND created_at < @Cutoff",
                new { Username = username, Cutoff = DateTime.UtcNow.AddMinutes(-10) });
        }

        public async Task<bool> EmailExistsAsync(string email)
        {
            using var connection = _dbConnection.CreateConnection();
            var count = await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM users WHERE LOWER(email) = LOWER(@Email)",
                new { Email = email });
            return count > 0;
        }

        public async Task<User> GetUserByIdAsync(int id)
        {
            using var connection = _dbConnection.CreateConnection();

            // 🔥 DODAJTE is_admin AS IsAdmin
            var sql = @"
        SELECT 
            id, 
            username, 
            first_name AS FirstName, 
            last_name AS LastName, 
            email, 
            phone, 
            password_hash AS PasswordHash, 
            birth_date AS BirthDate, 
            created_at AS CreatedAt,
            is_admin AS IsAdmin
        FROM users 
        WHERE id = @Id";

            return await connection.QueryFirstOrDefaultAsync<User>(sql, new { Id = id });
        }

        public async Task<User> GetUserByUsernameAsync(string username)
        {
            using var connection = _dbConnection.CreateConnection();

            var sql = "SELECT * FROM users WHERE LOWER(username) = LOWER(@Username)";
            return await connection.QueryFirstOrDefaultAsync<User>(sql, new { Username = username });
        }

        public async Task UpdateUserAsync(User user)
        {
            using var connection = _dbConnection.CreateConnection();

            var sql = @"
                UPDATE users 
                SET username = @Username,
                    first_name = @FirstName, 
                    last_name = @LastName, 
                    email = @Email, 
                    phone = @Phone, 
                    birth_date = @BirthDate
                WHERE id = @Id";

            await connection.ExecuteAsync(sql, new
            {
                user.Id,
                user.Username,
                user.FirstName,
                user.LastName,
                user.Email,
                user.Phone,
                BirthDate = user.BirthDate.HasValue
    ? user.BirthDate.Value.ToDateTime(TimeOnly.MinValue)
    : (DateTime?)null,
            });
        }

        public async Task DeleteUserAsync(int id)
        {
            using var connection = _dbConnection.CreateConnection();

            var sql = "DELETE FROM users WHERE id = @Id";
            await connection.ExecuteAsync(sql, new { Id = id });
        }

        public async Task<string?> GetUserAvatarAsync(int userId)
        {
            using var connection = _dbConnection.CreateConnection();

            var sql = @"
        SELECT avatar 
        FROM user_profiles 
        WHERE user_id = @UserId";

            return await connection.QueryFirstOrDefaultAsync<string>(sql, new { UserId = userId });
        }
    }
}