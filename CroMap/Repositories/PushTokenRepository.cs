using CroMap.Data;
using Dapper;

namespace CroMap.Repositories
{
    public interface IPushTokenRepository
    {
        /// <summary>Spremi token za korisnika; ako token već postoji, prelazi na ovog korisnika.</summary>
        Task UpsertAsync(int userId, string token, string? platform);

        Task RemoveAsync(int userId, string token);

        /// <summary>Svi tokeni zadanih korisnika (token → id korisnika).</summary>
        Task<IReadOnlyList<string>> GetTokensAsync(IReadOnlyCollection<int> userIds);

        /// <summary>Briše tokene koje je Expo prijavio kao nevažeće.</summary>
        Task RemoveTokensAsync(IReadOnlyCollection<string> tokens);
    }

    public class PushTokenRepository : IPushTokenRepository
    {
        private readonly DatabaseConnection _dbConnection;

        public PushTokenRepository(DatabaseConnection dbConnection)
        {
            _dbConnection = dbConnection;
        }

        public async Task UpsertAsync(int userId, string token, string? platform)
        {
            using var connection = _dbConnection.CreateConnection();
            await connection.ExecuteAsync(@"
                INSERT INTO push_tokens (user_id, token, platform)
                VALUES (@UserId, @Token, @Platform)
                ON CONFLICT (token)
                DO UPDATE SET user_id = EXCLUDED.user_id,
                              platform = EXCLUDED.platform,
                              last_seen_at = CURRENT_TIMESTAMP",
                new { UserId = userId, Token = token, Platform = platform });
        }

        public async Task RemoveAsync(int userId, string token)
        {
            using var connection = _dbConnection.CreateConnection();
            // Uvjet na user_id: korisnik smije maknuti samo vlastiti token.
            await connection.ExecuteAsync(
                "DELETE FROM push_tokens WHERE token = @Token AND user_id = @UserId",
                new { Token = token, UserId = userId });
        }

        public async Task<IReadOnlyList<string>> GetTokensAsync(IReadOnlyCollection<int> userIds)
        {
            if (userIds.Count == 0) return Array.Empty<string>();
            using var connection = _dbConnection.CreateConnection();
            var rows = await connection.QueryAsync<string>(
                "SELECT token FROM push_tokens WHERE user_id = ANY(@UserIds)",
                new { UserIds = userIds.ToArray() });
            return rows.ToList();
        }

        public async Task RemoveTokensAsync(IReadOnlyCollection<string> tokens)
        {
            if (tokens.Count == 0) return;
            using var connection = _dbConnection.CreateConnection();
            await connection.ExecuteAsync(
                "DELETE FROM push_tokens WHERE token = ANY(@Tokens)",
                new { Tokens = tokens.ToArray() });
        }
    }
}
