using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CroMap.Services;
using System.Security.Claims;

namespace CroMap.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UploadController : ControllerBase
    {
        private readonly IR2StorageService _storageService;
        // Greške idu u zapisnik; klijent dobiva samo da nije uspjelo.
        private readonly ILogger<UploadController> _logger;

        public UploadController(IR2StorageService storageService, ILogger<UploadController> logger)
        {
            _storageService = storageService;
            _logger = logger;
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)
                           ?? User.FindFirst("sub");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                throw new UnauthorizedAccessException("User not authenticated");
            return userId;
        }

        [HttpPost("media")]
        public async Task<IActionResult> UploadMedia(IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return BadRequest(new { message = "No file uploaded" });

                // Vrsta se utvrđuje iz PRVIH BAJTOVA datoteke, ne iz onoga što
                // je klijent naveo. ContentType i naziv datoteke dolaze iz
                // zahtjeva i oboje se slobodno izmišlja — tako je bilo moguće
                // poslati bilo što nazvano "slika.png" s ContentType
                // "image/png". Ono što je zaista poslano sada odlučuje i o
                // dopuštenoj vrsti, i o ograničenju veličine, i o nastavku.
                var sniffed = await SniffMediaTypeAsync(file);
                if (sniffed is null)
                    return BadRequest(new { message = "Invalid file type. Only images and videos are allowed." });

                var (contentType, extension, isVideo) = sniffed.Value;

                var maxSize = isVideo ? 50 * 1024 * 1024 : 10 * 1024 * 1024;
                if (file.Length > maxSize)
                    return BadRequest(new { message = $"File too large. Max {maxSize / 1024 / 1024}MB." });

                var fileName = $"{Guid.NewGuid()}_{DateTime.Now.Ticks}{extension}";

                string fileUrl;
                using (var stream = file.OpenReadStream())
                {
                    fileUrl = await _storageService.UploadFileAsync(
                        stream,
                        fileName,
                        contentType,
                        "stories"
                    );
                }

                return Ok(new
                {
                    url = fileUrl,
                    fileName,
                    type = isVideo ? "video" : "image"
                });
            }
            catch (Exception ex)
            {
                // Poruka iznimke je nekad išla ravno klijentu. Iz AWS SDK-a
                // ondje zna ispasti naziv kante, krajnja točka i slično —
                // podaci koji nemaju što tražiti u odgovoru. U zapisniku
                // ostaje sve, klijent dobiva samo da nije uspjelo.
                _logger.LogError(ex, "Slanje datoteke nije uspjelo.");
                return StatusCode(500, new { message = "Upload failed." });
            }
        }

        /// <summary>
        /// Utvrđuje vrstu datoteke iz njezinih prvih bajtova ("magični broj").
        /// Vraća <c>null</c> kad se ne prepozna nijedna dopuštena vrsta.
        /// </summary>
        /// <remarks>
        /// Potpisi: JPEG <c>FF D8 FF</c>, PNG <c>89 50 4E 47 0D 0A 1A 0A</c>,
        /// GIF <c>GIF87a</c>/<c>GIF89a</c>, a MP4 i MOV imaju na pomaku 4
        /// oznaku <c>ftyp</c> (ISO base media). Datoteka koja se ne prepozna
        /// odbija se — popustljivije bi značilo vratiti se na ono što je
        /// klijent naveo, a upravo to se ovdje izbjegava.
        /// </remarks>
        private static async Task<(string ContentType, string Extension, bool IsVideo)?> SniffMediaTypeAsync(
            IFormFile file)
        {
            var header = new byte[12];
            await using (var stream = file.OpenReadStream())
            {
                var read = await stream.ReadAsync(header.AsMemory(0, header.Length));
                if (read < 12) return null;
            }

            static bool Starts(byte[] buf, params byte[] sig)
            {
                if (buf.Length < sig.Length) return false;
                for (var i = 0; i < sig.Length; i++)
                    if (buf[i] != sig[i]) return false;
                return true;
            }

            if (Starts(header, 0xFF, 0xD8, 0xFF))
                return ("image/jpeg", ".jpg", false);

            if (Starts(header, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A))
                return ("image/png", ".png", false);

            if (Starts(header, (byte)'G', (byte)'I', (byte)'F', (byte)'8'))
                return ("image/gif", ".gif", false);

            // ISO base media (MP4, M4V, MOV): "ftyp" na pomaku 4, a marka na 8.
            if (header[4] == (byte)'f' && header[5] == (byte)'t'
                && header[6] == (byte)'y' && header[7] == (byte)'p')
            {
                var brand = System.Text.Encoding.ASCII.GetString(header, 8, 4);
                if (brand.StartsWith("qt", StringComparison.Ordinal))
                    return ("video/quicktime", ".mov", true);
                return ("video/mp4", ".mp4", true);
            }

            return null;
        }

    }
}