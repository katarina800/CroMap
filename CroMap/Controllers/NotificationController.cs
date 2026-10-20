using System.Security.Claims;
using CroMap.Models;
using CroMap.Repositories;
using CroMap.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CroMap.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class NotificationController : ControllerBase
    {
        private readonly INotificationRepository _repository;
        private readonly IPushTokenRepository _pushTokens;
        private readonly ILogger<NotificationController> _logger;

        public NotificationController(
            INotificationRepository repository,
            IPushTokenRepository pushTokens,
            ILogger<NotificationController> logger)
        {
            _repository = repository;
            _pushTokens = pushTokens;
            _logger = logger;
        }

        private int? CurrentUserId
        {
            get
            {
                var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                return int.TryParse(raw, out var id) ? (int?)id : null;
            }
        }

        public class PushTokenRequest
        {
            public string Token { get; set; } = string.Empty;
            public string? Platform { get; set; }
        }

        // POST: api/notification/push-token
        // Aplikacija javlja Expo push token uređaja nakon prijave.
        [HttpPost("push-token")]
        public async Task<IActionResult> RegisterPushToken([FromBody] PushTokenRequest request)
        {
            var userId = CurrentUserId;
            if (userId is null) return Unauthorized();

            var token = request?.Token?.Trim();
            if (!ExpoPushService.IsValidToken(token))
                return BadRequest(new { message = "Invalid push token" });

            var platform = request?.Platform;
            if (platform != null && platform.Length > 20) platform = platform.Substring(0, 20);

            await _pushTokens.UpsertAsync(userId.Value, token!, platform);
            return Ok(new { registered = true });
        }

        // DELETE: api/notification/push-token?token=ExponentPushToken[...]
        // Pri odjavi: uređaj više ne smije primati obavijesti ovog računa.
        [HttpDelete("push-token")]
        public async Task<IActionResult> UnregisterPushToken([FromQuery] string token)
        {
            var userId = CurrentUserId;
            if (userId is null) return Unauthorized();

            if (!ExpoPushService.IsValidToken(token))
                return BadRequest(new { message = "Invalid push token" });

            await _pushTokens.RemoveAsync(userId.Value, token.Trim());
            return Ok(new { removed = true });
        }

        // GET: api/notification?limit=30&offset=0
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] int limit = 30, [FromQuery] int offset = 0)
        {
            var userId = CurrentUserId;
            if (userId is null) return Unauthorized();

            if (limit < 1) limit = 1;
            if (limit > 100) limit = 100;
            if (offset < 0) offset = 0;

            var items = await _repository.GetForUserAsync(userId.Value, limit, offset);
            return Ok(items);
        }

        // GET: api/notification/unread-count
        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount()
        {
            var userId = CurrentUserId;
            if (userId is null) return Unauthorized();

            var count = await _repository.GetUnreadCountAsync(userId.Value);
            return Ok(new { count });
        }

        // POST: api/notification/5/read
        [HttpPost("{id}/read")]
        public async Task<IActionResult> MarkRead(int id)
        {
            var userId = CurrentUserId;
            if (userId is null) return Unauthorized();

            var ok = await _repository.MarkReadAsync(id, userId.Value);
            if (!ok) return NotFound(new { message = "Obavijest ne postoji." });
            return Ok(new { success = true });
        }

        // POST: api/notification/read-all
        [HttpPost("read-all")]
        public async Task<IActionResult> MarkAllRead()
        {
            var userId = CurrentUserId;
            if (userId is null) return Unauthorized();

            var updated = await _repository.MarkAllReadAsync(userId.Value);
            return Ok(new { success = true, updated });
        }

        // DELETE: api/notification/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = CurrentUserId;
            if (userId is null) return Unauthorized();

            var ok = await _repository.DeleteAsync(id, userId.Value);
            if (!ok) return NotFound(new { message = "Obavijest ne postoji." });
            return Ok(new { success = true });
        }

        // GET: api/notification/preferences
        [HttpGet("preferences")]
        public async Task<IActionResult> GetPreferences()
        {
            var userId = CurrentUserId;
            if (userId is null) return Unauthorized();

            var prefs = await _repository.GetPreferencesAsync(userId.Value);
            return Ok(prefs);
        }

        // PUT: api/notification/preferences
        [HttpPut("preferences")]
        public async Task<IActionResult> SavePreferences([FromBody] NotificationPreferencesDto prefs)
        {
            var userId = CurrentUserId;
            if (userId is null) return Unauthorized();
            if (prefs == null) return BadRequest(new { message = "Nedostaju postavke." });

            if (prefs.Categories != null && prefs.Categories.Count > 50)
                return BadRequest(new { message = "Previše kategorija." });
            if (!string.IsNullOrWhiteSpace(prefs.Email) && prefs.Email.Length > 254)
                return BadRequest(new { message = "Email je predugačak." });

            // Radijus se ne odbija nego popravlja: klijent starije verzije ga
            // uopće ne šalje (dođe 0), a odbiti cijelo spremanje zbog toga
            // značilo bi da korisnik ne može spremiti ni kategorije.
            prefs.RadiusKm = prefs.RadiusKm <= 0 ? 50 : Math.Clamp(prefs.RadiusKm, 1, 100);

            await _repository.SavePreferencesAsync(userId.Value, prefs);
            return Ok(new { success = true });
        }
    }
}
