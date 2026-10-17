using CroMap.Models;
using CroMap.ModelsDto;
using CroMap.Repositories;
using CroMap.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CroMap.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class VideoController : ControllerBase
    {
        private readonly IVideoRepository _videoRepository;
        private readonly IR2StorageService _storageService;
        private readonly INotificationRepository _notifications;
        private readonly IUserAreaRepository _userAreas;
        private readonly IEmailService _emailService;
        private readonly ILogger<VideoController> _logger;

        public VideoController(
            IVideoRepository videoRepository,
            IR2StorageService storageService,
            INotificationRepository notifications,
            IUserAreaRepository userAreas,
            IEmailService emailService,
            ILogger<VideoController> logger)
        {
            _videoRepository = videoRepository;
            _storageService = storageService;
            _notifications = notifications;
            _userAreas = userAreas;
            _emailService = emailService;
            _logger = logger;
        }

        private int? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)
                           ?? User.FindFirst("sub");

            if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
                return userId;

            return null;
        }

        // GET: api/video?page=1&pageSize=15&scope=local|global&radiusKm=50
        //      &lat=45.55&lon=18.69   (objave oko zadane točke)
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Video>>> GetAllVideos(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 15,
            [FromQuery] string scope = "global",
            [FromQuery] double radiusKm = 50,
            [FromQuery] double? lat = null,
            [FromQuery] double? lon = null)
        {
            var currentUserId = GetCurrentUserId();
            pageSize = Math.Clamp(pageSize, 1, 50);
            page = Math.Max(page, 1);
            radiusKm = Math.Clamp(radiusKm, 1, 500);

            List<(double Lat, double Lon)>? areas = null;

            // Zadana točka ima prednost pred "blizu mene".
            //
            // Kad korisnik na karti potraži drugi grad, zanima ga što se
            // događa TAMO, a ne u krajevima u kojima se inače kreće. Filtar
            // po udaljenosti je isti, mijenja se samo od čega se mjeri — pa
            // ovdje nema nove logike, samo druga polazišna točka.
            if (lat.HasValue && lon.HasValue)
            {
                areas = new List<(double Lat, double Lon)> { (lat.Value, lon.Value) };
            }
            else if (scope == "local" && currentUserId.HasValue)
            {
                var top = await _userAreas.GetTopAreasAsync(currentUserId.Value);
                areas = top.Select(a => (a.CellLat, a.CellLon)).ToList();

                // Korisnik kojeg aplikacija još nije nigdje vidjela nema
                // krajeva. Vratiti prazno bi izgledalo kao da nema sadržaja,
                // pa se u tom slučaju prikazuje sve — kao da je "svugdje".
                if (areas.Count == 0) areas = null;
            }

            var videos = await _videoRepository.GetAllVideosAsync(
                currentUserId, page, pageSize, areas, radiusKm);
            return Ok(videos);
        }

        /// <summary>
        /// Javlja gdje je korisnik trenutno. Zove se kad aplikacija ionako
        /// ima lokaciju (otvorena karta) — NE prati se u pozadini, jer bi to
        /// tražilo posebnu dozvolu na Play Storeu i trošilo bateriju.
        /// </summary>
        // POST: api/video/area-ping
        [HttpPost("area-ping")]
        public async Task<IActionResult> RecordArea([FromBody] AreaPingRequest request)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null) return Unauthorized();

            if (request == null
                || request.Latitude is < -90 or > 90
                || request.Longitude is < -180 or > 180)
                return BadRequest(new { message = "Neispravne koordinate" });

            await _userAreas.RecordAsync(currentUserId.Value, request.Latitude, request.Longitude);
            return Ok(new { message = "ok" });
        }

        // GET: api/video/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Video>> GetVideoById(int id)
        {
            var currentUserId = GetCurrentUserId();
            var video = await _videoRepository.GetVideoByIdAsync(id, currentUserId);

            if (video == null)
                return NotFound(new { message = "Video not found." });

            return Ok(video);
        }

        // GET: api/video/user/2
        [HttpGet("user/{userId}")]
        public async Task<ActionResult<IEnumerable<Video>>> GetVideosByUser(int userId)
        {
            var currentUserId = GetCurrentUserId();
            var videos = await _videoRepository.GetVideosByUserAsync(userId, currentUserId);
            return Ok(videos);
        }

        // POST: api/video
        [HttpPost]
        public async Task<IActionResult> CreateVideo([FromBody] Video video)
        {
            if (video == null || string.IsNullOrWhiteSpace(video.Title) || string.IsNullOrWhiteSpace(video.FilePath))
                return BadRequest("Invalid video data.");

            video.CreatedAt = DateTime.UtcNow;
            await _videoRepository.CreateVideoAsync(video);

            // Objava s mjesta je sama po sebi znak da je autor tamo bio.
            if (video.Latitude.HasValue && video.Longitude.HasValue)
            {
                try
                {
                    await _userAreas.RecordAsync(
                        video.UserId, video.Latitude.Value, video.Longitude.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Bilježenje područja korisnika nije uspjelo");
                }
            }
            return Ok(new { message = "Video created successfully.", videoId = video.Id });
        }

        // PUT: api/video
        [HttpPut]
        public async Task<IActionResult> UpdateVideo([FromBody] Video video)
        {
            if (video == null || video.Id <= 0)
                return BadRequest("Invalid video data.");

            var currentUserId = GetCurrentUserId();
            if (currentUserId != video.UserId)
                return Unauthorized(new { message = "You can only update your own videos." });

            await _videoRepository.UpdateVideoAsync(video);
            return Ok(new { message = "Video updated successfully." });
        }

        // DELETE: api/video/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteVideo(int id)
        {
            var currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
                return Unauthorized(new { message = "User not authenticated." });

            // Dohvati video prije brisanja da znamo file path za R2 cleanup
            var video = await _videoRepository.GetVideoByIdAsync(id, currentUserId);

            await _videoRepository.DeleteVideoAsync(id, currentUserId.Value);

            // Pokušaj obrisati i fajl s R2 (best effort, ne blokira response)
            if (video != null && !string.IsNullOrWhiteSpace(video.FilePath))
            {
                _ = _storageService.DeleteFileAsync(video.FilePath);
            }

            return Ok(new { message = "Video deleted successfully." });
        }

        // POST: api/video/upload
        [HttpPost("upload")]
        public async Task<IActionResult> UploadVideo([FromForm] VideoUploadRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Title) || request.Video == null)
                return BadRequest(new { message = "Invalid media data." });

            var currentUserId = GetCurrentUserId();
            if (currentUserId != request.UserId)
                return Unauthorized(new { message = "User ID mismatch." });

            var mediaType = request.MediaType ?? "video";
            var subFolder = mediaType == "image" ? "images" : "videos";

            var fileExtension = Path.GetExtension(request.Video.FileName);
            if (string.IsNullOrEmpty(fileExtension))
            {
                fileExtension = mediaType == "image" ? ".jpg" : ".mp4";
            }

            var fileName = $"{Guid.NewGuid()}_{DateTime.Now.Ticks}{fileExtension}";

            string mediaUrl;
            using (var stream = request.Video.OpenReadStream())
            {
                mediaUrl = await _storageService.UploadFileAsync(
                    stream,
                    fileName,
                    request.Video.ContentType,
                    subFolder
                );
            }

            // Sličica je neobavezna: stariji klijent je ne šalje, a slika je ne
            // treba. Ako slanje sličice padne, objava svejedno prolazi —
            // izgubi se samo pregled, ne i sam video.
            var thumbnailUrl = "";
            if (request.Thumbnail is { Length: > 0 } && mediaType != "image")
            {
                try
                {
                    var thumbExtension = Path.GetExtension(request.Thumbnail.FileName);
                    if (string.IsNullOrEmpty(thumbExtension)) thumbExtension = ".jpg";

                    var thumbName = $"{Guid.NewGuid()}_{DateTime.Now.Ticks}{thumbExtension}";
                    using var thumbStream = request.Thumbnail.OpenReadStream();
                    thumbnailUrl = await _storageService.UploadFileAsync(
                        thumbStream,
                        thumbName,
                        request.Thumbnail.ContentType,
                        "images"
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Slanje sličice videa nije uspjelo");
                }
            }

            var video = new Video
            {
                Title = request.Title,
                AdditionalDescription = request.Description ?? "",
                Location = request.Location ?? "",
                FilePath = mediaUrl,
                UserId = request.UserId,
                CreatedAt = DateTime.UtcNow,
                MediaType = mediaType,
                ThumbnailPath = thumbnailUrl,
                Categories = request.Categories ?? "",
                AgeGroups = request.AgeGroups ?? "",
                // Vrijeme početka ima smisla samo uz oznaku događaja —
                // inače bi objava bez datuma završila kao "događaj" bez
                // termina i visjela u popisu nadolazećeg.
                IsEvent = request.IsEvent && request.EventStartAt.HasValue,
                EventStartAt = request.IsEvent ? request.EventStartAt : null,
                Latitude = request.Latitude,
                Longitude = request.Longitude
            };

            await _videoRepository.CreateVideoAsync(video);

            // Obavijesti pratiteljima. Namjerno NE blokira odgovor: objava je
            // već spremljena i korisnik ne treba čekati razašiljanje, a ni
            // greška u slanju e-pošte ne smije srušiti upload.
            _ = NotifyFollowersAsync(video);

            return Ok(new
            {
                message = $"{mediaType} uploaded successfully.",
                mediaUrl,
                thumbnailUrl,
                videoId = video.Id,
                mediaType = mediaType
            });
        }

        /// <summary>
        /// Obavijesti o novoj objavi sve koje ona zanima — u aplikaciji i, za
        /// one koji su to tražili, e-poštom. Kriterij je kategorija koju su
        /// odabrali, ograničena blizinom; praćenje autora više nije uvjet.
        /// </summary>
        private async Task NotifyFollowersAsync(Video video)
        {
            try
            {
                var categories = (video.Categories ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => c.Trim())
                    .Where(c => c.Length > 0)
                    .Distinct()
                    .ToArray();

                if (categories.Length == 0) return;

                var title = string.IsNullOrWhiteSpace(video.Title) ? "Nova objava" : video.Title;
                var body = video.Location ?? "";

                // Položaj objave odlučuje kome je "blizu". Bez njega obavijest
                // ide svima koje kategorija zanima — radije previše nego da
                // objava bez koordinata nikome ne stigne.
                await _notifications.FanOutNewActivityAsync(
                    video.UserId, video.Id, title, body, categories,
                    video.Latitude, video.Longitude);

                var recipients = await _notifications.GetEmailRecipientsAsync(
                    video.UserId, categories, video.Latitude, video.Longitude);

                foreach (var recipient in recipients)
                {
                    try
                    {
                        await _emailService.SendEmailAsync(
                            recipient.ToEmail,
                            BuildEmailSubject(recipient.Language, title),
                            BuildEmailBody(recipient.Language, recipient.FirstName, title, body));
                    }
                    catch (Exception ex)
                    {
                        // Jedna neuspjela adresa ne smije zaustaviti ostale.
                        _logger.LogWarning(ex,
                            "Notification email failed for user {UserId}", recipient.UserId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notification fan-out failed for video {VideoId}", video.Id);
            }
        }

        private static string BuildEmailSubject(string language, string title) => language switch
        {
            "en" => $"New activity on VARA: {title}",
            "de" => $"Neue Aktivität auf VARA: {title}",
            "fr" => $"Nouvelle activité sur VARA : {title}",
            "it" => $"Nuova attività su VARA: {title}",
            _ => $"Nova aktivnost na VARA-i: {title}",
        };

        private static string BuildEmailBody(string language, string firstName, string title, string location)
        {
            var (greeting, intro, whereLabel, footer) = language switch
            {
                "en" => ($"Hi {firstName},", "someone you follow has just posted something new:", "Location", "You are receiving this because you turned on e-mail notifications for this category in VARA."),
                "de" => ($"Hallo {firstName},", "jemand, dem du folgst, hat gerade etwas Neues gepostet:", "Ort", "Du erhältst diese E-Mail, weil du in VARA E-Mail-Benachrichtigungen für diese Kategorie aktiviert hast."),
                "fr" => ($"Bonjour {firstName},", "une personne que vous suivez vient de publier quelque chose :", "Lieu", "Vous recevez cet e-mail car vous avez activé les notifications par e-mail pour cette catégorie dans VARA."),
                "it" => ($"Ciao {firstName},", "una persona che segui ha appena pubblicato qualcosa di nuovo:", "Luogo", "Ricevi questa e-mail perché hai attivato le notifiche via e-mail per questa categoria in VARA."),
                _ => ($"Bok {firstName},", "korisnik kojeg pratiš upravo je objavio nešto novo:", "Lokacija", "Ovu poruku primaš jer si u VARA-i uključio/la obavijesti e-poštom za ovu kategoriju."),
            };

            var safeTitle = System.Net.WebUtility.HtmlEncode(title ?? "");
            var safeLocation = System.Net.WebUtility.HtmlEncode(location ?? "");
            var locationBlock = string.IsNullOrWhiteSpace(safeLocation)
                ? ""
                : $"<p style=\"margin:0 0 16px;color:#4a5a44;\">{whereLabel}: {safeLocation}</p>";

            return $@"
<div style=""font-family:-apple-system,Segoe UI,Roboto,sans-serif;max-width:520px;margin:0 auto;padding:24px;"">
  <p style=""margin:0 0 12px;font-size:16px;color:#1d2b18;"">{greeting}</p>
  <p style=""margin:0 0 16px;color:#4a5a44;"">{intro}</p>
  <h2 style=""margin:0 0 8px;font-size:20px;color:#2D6418;"">{safeTitle}</h2>
  {locationBlock}
  <hr style=""border:none;border-top:1px solid #d8e3d0;margin:24px 0;"">
  <p style=""margin:0;font-size:12px;color:#8A9486;"">{footer}</p>
</div>";
        }
    }
}