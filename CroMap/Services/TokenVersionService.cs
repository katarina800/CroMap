using CroMap.Data;
using Dapper;
using Microsoft.Extensions.Caching.Memory;

namespace CroMap.Services
{
    /// <summary>
    /// Verzija sesije korisnika (users.token_version). Svaki izdani JWT nosi
    /// verziju iz trenutka prijave ("tv"); kad se lozinka resetira, verzija se
    /// poveća pa svi ranije izdani tokeni (na svim uređajima) odmah prestaju
    /// vrijediti — iako im rok od 7 dana još nije istekao.
    ///
    /// Čita se iz baze, ali se kratko pamti u memoriji da svaki zahtjev ne bi
    /// otvarao dodatan upit. Na instanci na kojoj se reset dogodio pamćenje se
    /// briše odmah; na ostalim instancama odjava stupa na snagu najkasnije
    /// nakon <see cref="CacheTtl"/>.
    /// </summary>
    public class TokenVersionService
    {
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

        private readonly DatabaseConnection _db;
        private readonly IMemoryCache _cache;

        public TokenVersionService(DatabaseConnection db, IMemoryCache cache)
        {
            _db = db;
            _cache = cache;
        }

        /// <summary>Trenutna verzija, ili null ako korisnik više ne postoji.</summary>
        public async Task<int?> GetAsync(int userId)
        {
            if (_cache.TryGetValue(CacheKey(userId), out int? cached))
                return cached;

            using var conn = _db.CreateConnection();
            var version = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT token_version FROM users WHERE id = @Id", new { Id = userId });

            _cache.Set(CacheKey(userId), version, CacheTtl);
            return version;
        }

        /// <summary>Zaboravlja pamćenu verziju (nakon što je u bazi povećana).</summary>
        public void Invalidate(int userId) => _cache.Remove(CacheKey(userId));

        private static string CacheKey(int userId) => $"token-version:{userId}";
    }
}
