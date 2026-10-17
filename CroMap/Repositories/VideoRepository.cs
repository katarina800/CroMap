using CroMap.Models;
using Dapper;
using System.Data;
using CroMap.Data;

namespace CroMap.Repositories
{
    public class VideoRepository : IVideoRepository
    {
        private readonly DatabaseConnection _dbConnection;

        public VideoRepository(DatabaseConnection dbConnection)
        {
            _dbConnection = dbConnection;
        }

        /// <param name="areas">
        /// Krajevi korisnika. Kad je predano barem jedno područje, vraćaju se
        /// samo objave unutar <paramref name="radiusKm"/> od nekog od njih.
        /// Prazno znači "sve objave" (prikaz "svugdje").
        /// </param>
        public async Task<IEnumerable<Video>> GetAllVideosAsync(
            int? currentUserId,
            int page = 1,
            int pageSize = 15,
            IEnumerable<(double Lat, double Lon)>? areas = null,
            double radiusKm = 50)
        {
            using var connection = _dbConnection.CreateConnection();

            // Paginirano — prije se cijela tablica videa vraćala u jednom
            // odgovoru na svako otvaranje feeda, što je postajalo sve sporije
            // (i teže za memoriju na klijentu) kako je raslo videa.
            var offset = (Math.Max(page, 1) - 1) * pageSize;

            var sql = @"
        SELECT
            v.*,
            u.username as UserName,
            u.first_name as UserFirstName,
            u.last_name as UserLastName,
            COALESCE(p.avatar, '') as UserAvatar,
            COALESCE(lc.like_count, 0) as LikeCount,
            COALESCE(cc.comment_count, 0) as CommentCount,
            CASE WHEN ul.user_id IS NOT NULL THEN true ELSE false END as IsLiked,
            CASE WHEN sv.user_id IS NOT NULL THEN true ELSE false END as IsSaved,
            CASE WHEN v.user_id = @CurrentUserId THEN true ELSE false END as IsOwner
        FROM videos v
        LEFT JOIN users u ON v.user_id = u.id
        LEFT JOIN user_profiles p ON p.user_id = v.user_id
        LEFT JOIN (
            SELECT video_id, COUNT(*) as like_count
            FROM likes
            GROUP BY video_id
        ) lc ON v.id = lc.video_id
        LEFT JOIN (
            SELECT video_id, COUNT(*) as comment_count
            FROM comments
            GROUP BY video_id
        ) cc ON v.id = cc.video_id
        LEFT JOIN likes ul ON v.id = ul.video_id AND ul.user_id = @CurrentUserId
        LEFT JOIN saved_videos sv ON v.id = sv.video_id AND sv.user_id = @CurrentUserId
        LEFT JOIN follows fw ON fw.followed_id = v.user_id AND fw.follower_id = @CurrentUserId
        /**WHERE**/
        -- Objave ljudi koje pratiš idu više.
        --
        -- Feed je bio čisto kronološki, pa je praćenje nekoga značilo samo to
        -- da mu se ime pojavi u popisu praćenih — na ono što vidiš nije
        -- utjecalo ni najmanje.
        --
        -- Umjesto da se praćeni dignu iznad svega (čime bi objava od prošlog
        -- mjeseca pretekla današnju), njihovoj se objavi doda tri dana
        -- prednosti. Ostaje iznad nepraćenih sve dok ove nisu više od tri
        -- dana svježije, a redoslijed i dalje ima smisla u vremenu.
        ORDER BY (v.created_at + CASE WHEN fw.follower_id IS NOT NULL
                                      THEN INTERVAL '3 days'
                                      ELSE INTERVAL '0 days' END) DESC,
                 -- Stabilan poredak za paginaciju: bez ovoga dvije objave s
                 -- istim vremenom znaju zamijeniti mjesta između stranica i
                 -- tada se jedna pojavi dvaput, a druga nikad.
                 v.id DESC
        LIMIT @PageSize OFFSET @Offset";

            var areaList = areas?.ToList() ?? new List<(double Lat, double Lon)>();
            if (areaList.Count > 0)
            {
                // Udaljenost se računa preko kvadrata razlike u stupnjevima,
                // skalirane na kilometre: stupanj geografske širine je svugdje
                // ~111 km, a dužine se skraćuje prema polovima (otud cos).
                // Dovoljno točno za "je li ovo u mom kraju" i ne traži PostGIS.
                var clauses = areaList.Select((_, i) => $@"
                    (POWER((v.latitude - @Lat{i}) * 111.0, 2)
                     + POWER((v.longitude - @Lon{i}) * 111.0 * COS(RADIANS(@Lat{i})), 2))
                    <= POWER(@RadiusKm, 2)");

                sql = sql.Replace(
                    "/**WHERE**/",
                    "WHERE v.latitude IS NOT NULL AND (" + string.Join(" OR ", clauses) + ")");
            }
            else
            {
                sql = sql.Replace("/**WHERE**/", "");
            }

            var parameters = new DynamicParameters(new
            {
                CurrentUserId = currentUserId,
                PageSize = pageSize,
                Offset = offset,
                RadiusKm = radiusKm
            });
            for (var i = 0; i < areaList.Count; i++)
            {
                parameters.Add($"Lat{i}", areaList[i].Lat);
                parameters.Add($"Lon{i}", areaList[i].Lon);
            }

            var videos = await connection.QueryAsync<Video>(sql, parameters);
            return videos;
        }

        public async Task<Video> GetVideoByIdAsync(int id, int? currentUserId)
        {
            using var connection = _dbConnection.CreateConnection();

            var sql = @"
                SELECT 
                    v.*,
                    u.username as UserName,
                    u.first_name as UserFirstName,
                    u.last_name as UserLastName,
                    COALESCE(p.avatar, '') as UserAvatar,
                    COALESCE(lc.like_count, 0) as LikeCount,
                    COALESCE(cc.comment_count, 0) as CommentCount,
                    CASE WHEN ul.user_id IS NOT NULL THEN true ELSE false END as IsLiked,
                    CASE WHEN sv.user_id IS NOT NULL THEN true ELSE false END as IsSaved,
                    CASE WHEN v.user_id = @CurrentUserId THEN true ELSE false END as IsOwner
                FROM videos v
                LEFT JOIN users u ON v.user_id = u.id
                LEFT JOIN user_profiles p ON p.user_id = v.user_id
                LEFT JOIN (
                    SELECT video_id, COUNT(*) as like_count
                    FROM likes
                    WHERE video_id = @Id
                    GROUP BY video_id
                ) lc ON v.id = lc.video_id
                LEFT JOIN (
                    SELECT video_id, COUNT(*) as comment_count
                    FROM comments
                    WHERE video_id = @Id
                    GROUP BY video_id
                ) cc ON v.id = cc.video_id
                LEFT JOIN likes ul ON v.id = ul.video_id AND ul.user_id = @CurrentUserId
                LEFT JOIN saved_videos sv ON v.id = sv.video_id AND sv.user_id = @CurrentUserId
                WHERE v.id = @Id";

            var video = await connection.QueryFirstOrDefaultAsync<Video>(sql, new { Id = id, CurrentUserId = currentUserId });
            return video;
        }

        public async Task<IEnumerable<Video>> GetVideosByUserAsync(int userId, int? currentUserId)
        {
            using var connection = _dbConnection.CreateConnection();

            var sql = @"
        SELECT 
            v.*,
            u.username as UserName,
            u.first_name as UserFirstName,
            u.last_name as UserLastName,
            COALESCE(p.avatar, '') as UserAvatar,
            COALESCE(lc.like_count, 0) as LikeCount,
            COALESCE(cc.comment_count, 0) as CommentCount,
            CASE WHEN ul.user_id IS NOT NULL THEN true ELSE false END as IsLiked,
            CASE WHEN sv.user_id IS NOT NULL THEN true ELSE false END as IsSaved,
            CASE WHEN wv.user_id IS NOT NULL THEN true ELSE false END as IsInWishlist,
            CASE WHEN v.user_id = @CurrentUserId THEN true ELSE false END as IsOwner
        FROM videos v
        LEFT JOIN users u ON v.user_id = u.id
        LEFT JOIN user_profiles p ON p.user_id = v.user_id
        LEFT JOIN (
            SELECT video_id, COUNT(*) as like_count
            FROM likes
            GROUP BY video_id
        ) lc ON v.id = lc.video_id
        LEFT JOIN (
            SELECT video_id, COUNT(*) as comment_count
            FROM comments
            GROUP BY video_id
        ) cc ON v.id = cc.video_id
        LEFT JOIN likes ul ON v.id = ul.video_id AND ul.user_id = @CurrentUserId
        LEFT JOIN saved_videos sv ON v.id = sv.video_id AND sv.user_id = @CurrentUserId
        LEFT JOIN wishlist_videos wv ON v.id = wv.video_id AND wv.user_id = @CurrentUserId
        WHERE v.user_id = @UserId
        ORDER BY v.created_at DESC";

            var videos = await connection.QueryAsync<Video>(sql, new { UserId = userId, CurrentUserId = currentUserId });
            return videos;
        }

        public async Task CreateVideoAsync(Video video)
        {
            using var connection = _dbConnection.CreateConnection();

            var sql = @"
        INSERT INTO videos (user_id, title, location, additional_description, file_path, created_at, media_type, thumbnail_path, categories, age_groups, is_event, event_start_at, latitude, longitude)
        VALUES (@UserId, @Title, @Location, @AdditionalDescription, @FilePath, @CreatedAt, @MediaType, @ThumbnailPath, @Categories, @AgeGroups, @IsEvent, @EventStartAt, @Latitude, @Longitude)
        RETURNING id";

            video.Id = await connection.ExecuteScalarAsync<int>(sql, video);
        }

        public async Task UpdateVideoAsync(Video video)
        {
            using var connection = _dbConnection.CreateConnection();

            var sql = @"
                UPDATE videos 
                SET title = @Title, 
                    location = @Location, 
                    additional_description = @AdditionalDescription,
                    file_path = @FilePath
                WHERE id = @Id AND user_id = @UserId";

            await connection.ExecuteAsync(sql, video);
        }

        public async Task DeleteVideoAsync(int id, int userId)
        {
            using var connection = _dbConnection.CreateConnection();

            var sql = "DELETE FROM videos WHERE id = @Id AND user_id = @UserId";
            await connection.ExecuteAsync(sql, new { Id = id, UserId = userId });
        }

        // LIKE METODE
        public async Task<bool> ToggleLikeAsync(int videoId, int userId)
        {
            using var connection = _dbConnection.CreateConnection();

            // Prvo provjeri postoji li like
            var checkSql = "SELECT id FROM likes WHERE video_id = @VideoId AND user_id = @UserId";
            var existingLike = await connection.QueryFirstOrDefaultAsync<int?>(checkSql, new { VideoId = videoId, UserId = userId });

            if (existingLike.HasValue)
            {
                // Ako postoji, obriši
                var deleteSql = "DELETE FROM likes WHERE video_id = @VideoId AND user_id = @UserId";
                await connection.ExecuteAsync(deleteSql, new { VideoId = videoId, UserId = userId });
                return false; // unlike
            }
            else
            {
                // Ako ne postoji, dodaj
                var insertSql = "INSERT INTO likes (user_id, video_id, created_at) VALUES (@UserId, @VideoId, @CreatedAt)";
                await connection.ExecuteAsync(insertSql, new { UserId = userId, VideoId = videoId, CreatedAt = DateTime.UtcNow });
                return true; // like
            }
        }

        public async Task<int> GetLikeCountAsync(int videoId)
        {
            using var connection = _dbConnection.CreateConnection();

            var sql = "SELECT COUNT(*) FROM likes WHERE video_id = @VideoId";
            return await connection.ExecuteScalarAsync<int>(sql, new { VideoId = videoId });
        }

        public async Task<bool> IsLikedByUserAsync(int videoId, int userId)
        {
            using var connection = _dbConnection.CreateConnection();

            var sql = "SELECT COUNT(*) FROM likes WHERE video_id = @VideoId AND user_id = @UserId";
            var count = await connection.ExecuteScalarAsync<int>(sql, new { VideoId = videoId, UserId = userId });
            return count > 0;
        }

        // SAVE VIDEO METODE
        public async Task<bool> SaveVideoAsync(int videoId, int userId)
        {
            using var connection = _dbConnection.CreateConnection();

            var sql = @"
        INSERT INTO saved_videos (video_id, user_id, saved_at)
        VALUES (@VideoId, @UserId, @SavedAt)
        ON CONFLICT (user_id, video_id) DO NOTHING";

            var rowsAffected = await connection.ExecuteAsync(sql, new
            {
                VideoId = videoId,
                UserId = userId,
                SavedAt = DateTime.UtcNow
            });

            return rowsAffected > 0;
        }

        public async Task<bool> UnsaveVideoAsync(int videoId, int userId)
        {
            using var connection = _dbConnection.CreateConnection();

            var sql = "DELETE FROM saved_videos WHERE video_id = @VideoId AND user_id = @UserId";

            var rowsAffected = await connection.ExecuteAsync(sql, new { VideoId = videoId, UserId = userId });

            return rowsAffected > 0;
        }

        public async Task<bool> IsSavedByUserAsync(int videoId, int userId)
        {
            using var connection = _dbConnection.CreateConnection();

            var sql = "SELECT COUNT(*) FROM saved_videos WHERE video_id = @VideoId AND user_id = @UserId";
            var count = await connection.ExecuteScalarAsync<int>(sql, new { VideoId = videoId, UserId = userId });
            return count > 0;
        }

        
        public async Task<int> AddCommentAsync(Comment comment)
        {
            using var connection = _dbConnection.CreateConnection();

            var sql = @"
        INSERT INTO comments (user_id, video_id, content, created_at)
        VALUES (@UserId, @VideoId, @Content, @CreatedAt)
        RETURNING id";

            var commentId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                UserId = comment.UserId,
                VideoId = comment.VideoId,
                Content = comment.Content,
                CreatedAt = comment.CreatedAt
            });

            return commentId;
        }

        public async Task<IEnumerable<Comment>> GetCommentsByVideoIdAsync(int videoId)
        {
            using var connection = _dbConnection.CreateConnection();

            var sql = @"
                SELECT c.*, u.username as UserName
                FROM comments c
                LEFT JOIN users u ON c.user_id = u.id
                WHERE c.video_id = @VideoId
                ORDER BY c.created_at DESC";

            var comments = await connection.QueryAsync<Comment>(sql, new { VideoId = videoId });
            return comments;
        }

        public async Task<int> GetCommentCountAsync(int videoId)
        {
            using var connection = _dbConnection.CreateConnection();

            var sql = "SELECT COUNT(*) FROM comments WHERE video_id = @VideoId";
            return await connection.ExecuteScalarAsync<int>(sql, new { VideoId = videoId });
        }
    }
}