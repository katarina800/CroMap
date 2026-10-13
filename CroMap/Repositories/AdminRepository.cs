using CroMap.Data;
using Dapper;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CroMap.Repositories
{
    public class AdminRepository
    {
        private readonly DatabaseConnection _dbConnection;

        public AdminRepository(DatabaseConnection dbConnection)
        {
            _dbConnection = dbConnection;
        }

        public async Task SeedAdminUser()
        {
            // Lozinka admina više nije zapisana u kodu. Dosad je stajala ovdje
            // doslovno ("Admin@CroMap2024!@#"), a repozitorij je u gitu — tko
            // god ga vidi mogao se prijaviti kao administrator. Sada dolazi iz
            // varijable okoline ADMIN_SEED_PASSWORD; ako nije postavljena,
            // admin se jednostavno ne kreira (postojeći ostaje netaknut).
            var seedPassword = Environment.GetEnvironmentVariable("ADMIN_SEED_PASSWORD");

            using var connection = _dbConnection.CreateConnection();

            var adminEmail = Environment.GetEnvironmentVariable("ADMIN_SEED_EMAIL")
                             ?? "admin@cromap.com";

            // Provjeri postoji li admin
            var checkQuery = "SELECT COUNT(*) FROM users WHERE email = @Email";
            var exists = await connection.ExecuteScalarAsync<int>(checkQuery, new { Email = adminEmail });

            if (exists == 0)
            {
                if (string.IsNullOrWhiteSpace(seedPassword) || seedPassword.Length < 12)
                {
                    Console.WriteLine(
                        "[SeedAdminUser] ADMIN_SEED_PASSWORD nije postavljen (ili je kraći " +
                        "od 12 znakova) — administrator nije kreiran.");
                    return;
                }

                var passwordHash = BCrypt.Net.BCrypt.HashPassword(seedPassword);

                var insertQuery = @"
            INSERT INTO users (email, username, first_name, last_name, password_hash, birth_date, is_admin, email_verified, created_at)
            VALUES (@Email, @Username, @FirstName, @LastName, @PasswordHash, @BirthDate, true, true, NOW())";

                await connection.ExecuteAsync(insertQuery, new
                {
                    Email = adminEmail,
                    Username = "admin_cromap",
                    FirstName = "Admin",
                    LastName = "CroMap",
                    PasswordHash = passwordHash,
                    BirthDate = new DateTime(1990, 1, 1) // Default datum rođenja
                });
            }
        }


        // Dohvati sve korisnike sa statistikama
        public async Task<IEnumerable<AdminUserDto>> GetAllUsersWithStatsAsync()
        {
            using var connection = _dbConnection.CreateConnection();

            var sql = @"
        SELECT 
            u.id,
            u.first_name as FirstName,
            u.last_name as LastName,
            u.username,
            u.email,
            u.created_at as CreatedAt,
            COALESCE(video_stats.total_posts, 0) as TotalPosts,
            COALESCE(video_stats.total_likes, 0) as TotalLikes,
            COALESCE(video_stats.total_comments, 0) as TotalComments,
            COALESCE(SUM(a.session_minutes), 0) as TotalSessionMinutes,
            COALESCE(followers_count.cnt, 0) as FollowersCount,
            COALESCE(following_count.cnt, 0) as FollowingCount
        FROM users u
        LEFT JOIN (
            SELECT 
                v.user_id,
                COUNT(DISTINCT v.id) as total_posts,
                COUNT(DISTINCT l.id) as total_likes,
                COUNT(DISTINCT c.id) as total_comments
            FROM videos v
            LEFT JOIN likes l ON l.video_id = v.id
            LEFT JOIN comments c ON c.video_id = v.id
            GROUP BY v.user_id
        ) video_stats ON video_stats.user_id = u.id
        LEFT JOIN activity_logs a ON a.user_id = u.id
        LEFT JOIN (
            SELECT followed_id, COUNT(*) as cnt
            FROM follows
            GROUP BY followed_id
        ) followers_count ON followers_count.followed_id = u.id
        LEFT JOIN (
            SELECT follower_id, COUNT(*) as cnt
            FROM follows
            GROUP BY follower_id
        ) following_count ON following_count.follower_id = u.id
        GROUP BY 
            u.id, u.first_name, u.last_name, u.username, u.email, u.created_at,
            video_stats.total_posts, video_stats.total_likes, video_stats.total_comments,
            followers_count.cnt, following_count.cnt
        ORDER BY u.created_at DESC";

            return await connection.QueryAsync<AdminUserDto>(sql);
        }

        // Dohvati admin summary statistiku
        public async Task<AdminSummaryDto> GetAdminSummaryAsync()
        {
            using var connection = _dbConnection.CreateConnection();

            var sql = @"
        SELECT 
            COUNT(DISTINCT u.id) as TotalUsers,
            COALESCE((SELECT COUNT(*) FROM likes), 0) as TotalLikes,
            COALESCE((SELECT COUNT(*) FROM comments), 0) as TotalComments,
            COALESCE(SUM(a.session_minutes), 0) as TotalMinutes
        FROM users u
        LEFT JOIN activity_logs a ON a.user_id = u.id";

            return await connection.QueryFirstOrDefaultAsync<AdminSummaryDto>(sql);
        }

        // Spremi ocjenu plana
        public async Task<int> SavePlanRatingAsync(string userName, string destination, int rating)
        {
            using var connection = _dbConnection.CreateConnection();
            var sql = @"
        INSERT INTO plan_ratings (user_name, destination, rating, created_at)
        VALUES (@UserName, @Destination, @Rating, NOW())
        RETURNING id";
            return await connection.ExecuteScalarAsync<int>(sql, new { UserName = userName, Destination = destination, Rating = rating });
        }

        // Dohvati sve ocjene planova (za admin)
        public async Task<IEnumerable<PlanRatingDto>> GetPlanRatingsAsync()
        {
            using var connection = _dbConnection.CreateConnection();
            var sql = @"
        SELECT id, user_name as UserName, destination, rating, created_at as CreatedAt
        FROM plan_ratings
        ORDER BY created_at DESC";
            return await connection.QueryAsync<PlanRatingDto>(sql);
        }

        // Spremi support report
        public async Task SaveSupportReportAsync(SupportReportRequest req)
        {
            using var connection = _dbConnection.CreateConnection();
            var sql = @"
        INSERT INTO support_reports (type, message, user_name, username, created_at)
        VALUES (@Type, @Message, @UserName, @UserUsername, NOW())";
            await connection.ExecuteAsync(sql, req);
        }

        // Dohvati sve support reporte (za admin)
        public async Task<IEnumerable<SupportReportDto>> GetSupportReportsAsync()
        {
            using var connection = _dbConnection.CreateConnection();
            var sql = @"
        SELECT id, type, message,
       user_name as UserName,
       username as UserUsername,
       created_at as CreatedAt
FROM support_reports
ORDER BY created_at DESC";
            return await connection.QueryAsync<SupportReportDto>(sql);
        }
    }

    public class AdminUserDto
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int TotalPosts { get; set; }
        public int TotalLikes { get; set; }
        public int TotalComments { get; set; }
        public int TotalSessionMinutes { get; set; }
        public int FollowersCount { get; set; }
        public int FollowingCount { get; set; }
    }

    public class AdminSummaryDto
    {
        public int TotalUsers { get; set; }
        public int TotalLikes { get; set; }
        public int TotalComments { get; set; }
        public int TotalMinutes { get; set; }
    }

    public class PlanRatingDto
    {
        public int Id { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public int Rating { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class SavePlanRatingRequest
    {
        public string UserName { get; set; } = string.Empty;
        public string Destination { get; set; } = string.Empty;
        public int Rating { get; set; }
    }

    public class SupportReportRequest
    {
        public string Type { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string UserUsername { get; set; } = string.Empty;
    }

    public class SupportReportDto
    {
        public int Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string UserUsername { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }


}