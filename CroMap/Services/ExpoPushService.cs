using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CroMap.Repositories;

namespace CroMap.Services
{
    public interface IExpoPushService
    {
        /// <summary>
        /// Pošalji push obavijest svim uređajima zadanih korisnika. Nikad ne baca
        /// iznimku: obavijest je dodatak, i njezin neuspjeh ne smije srušiti
        /// radnju zbog koje se šalje (objavu, poruku…).
        /// </summary>
        Task SendAsync(IEnumerable<int> userIds, string title, string body, object? data = null);
    }

    /// <summary>
    /// Šalje push obavijesti preko Expo Push API-ja (https://exp.host). Expo sam
    /// razvodi prema Googleovom (FCM) i Appleovom (APNs) servisu, pa poslužitelju
    /// ne trebaju njihovi ključevi — oni se postavljaju u Expo projektu.
    ///
    /// Neobavezno: konfiguracija ExpoPush:AccessToken (env ExpoPush__AccessToken)
    /// uključuje "enhanced security" provjeru pri slanju.
    /// </summary>
    public class ExpoPushService : IExpoPushService
    {
        private const string SendUrl = "https://exp.host/--/api/v2/push/send";
        private const int MaxPerRequest = 100; // ograničenje Expo API-ja

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IPushTokenRepository _tokens;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ExpoPushService> _logger;

        public ExpoPushService(
            IHttpClientFactory httpClientFactory,
            IPushTokenRepository tokens,
            IConfiguration configuration,
            ILogger<ExpoPushService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _tokens = tokens;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>Expo tokeni izgledaju ExponentPushToken[...] ili ExpoPushToken[...].</summary>
        public static bool IsValidToken(string? token) =>
            !string.IsNullOrWhiteSpace(token)
            && token.Length <= 200
            && (token.StartsWith("ExponentPushToken[", StringComparison.Ordinal)
                || token.StartsWith("ExpoPushToken[", StringComparison.Ordinal))
            && token.EndsWith("]", StringComparison.Ordinal);

        public async Task SendAsync(IEnumerable<int> userIds, string title, string body, object? data = null)
        {
            try
            {
                var ids = userIds.Distinct().ToList();
                if (ids.Count == 0) return;

                var tokens = await _tokens.GetTokensAsync(ids);
                if (tokens.Count == 0) return;

                var invalid = new List<string>();
                foreach (var chunk in tokens.Chunk(MaxPerRequest))
                {
                    await SendChunkAsync(chunk, title, body, data, invalid);
                }

                if (invalid.Count > 0)
                    await _tokens.RemoveTokensAsync(invalid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Push notification sending failed");
            }
        }

        private async Task SendChunkAsync(
            string[] tokens, string title, string body, object? data, List<string> invalid)
        {
            var messages = tokens.Select(t => new
            {
                to = t,
                title = Truncate(title, 100),
                body = Truncate(body, 200),
                data,
                sound = "default",
                channelId = "default",
                priority = "high",
            });

            var request = new HttpRequestMessage(HttpMethod.Post, SendUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(messages), Encoding.UTF8, "application/json"),
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var accessToken = _configuration["ExpoPush:AccessToken"];
            if (!string.IsNullOrWhiteSpace(accessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            using var response = await client.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Expo push returned {Status}: {Body}", (int)response.StatusCode, Truncate(json, 300));
                return;
            }

            // Odgovor ima jednu "ulaznicu" po poruci, istim redoslijedom. Uređaj koji
            // više ne postoji (aplikacija obrisana, token istekao) javlja se kao
            // DeviceNotRegistered — takav token treba obrisati, inače se zauvijek
            // šalje u prazno.
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var tickets) || tickets.ValueKind != JsonValueKind.Array)
                return;

            var i = 0;
            foreach (var ticket in tickets.EnumerateArray())
            {
                if (i >= tokens.Length) break;
                if (ticket.TryGetProperty("status", out var status) && status.GetString() == "error"
                    && ticket.TryGetProperty("details", out var details)
                    && details.TryGetProperty("error", out var error)
                    && error.GetString() == "DeviceNotRegistered")
                {
                    invalid.Add(tokens[i]);
                }
                i++;
            }
        }

        private static string Truncate(string? value, int max)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= max ? value : value.Substring(0, max);
        }
    }
}
