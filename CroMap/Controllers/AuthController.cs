using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CroMap.Data;
using CroMap.Models;
using CroMap.Repositories;
using CroMap.Services;
using Dapper;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

namespace CroMap.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly UserRepository _repo;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;
        private readonly DatabaseConnection _dbConnection;
        private readonly IEmailService _emailService;
        private readonly PasswordResetRepository _resetRepo;
        private readonly EmailVerificationRepository _verifyRepo;
        private readonly TokenVersionService _tokenVersions;
        // Brisanje računa: redak u bazi ide kroz ProfileRepository, a medijske
        // datoteke iz pohrane kroz R2.
        private readonly IProfileRepository _profileRepository;
        private readonly IR2StorageService _storageService;

        // ─── VARA logo: CID inline embedding ─────────────────────────────────
        private const string _logo80Cid = "vara_logo_80";
        private const string _logo36Cid = "vara_logo_36";
        private const string _logo80B64 = "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";
        private const string _logo36B64 = "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";

        private static string LogoImg(string cid, int px)
        {
            var radius = px / 6;
            return $"<img src=\"cid:{cid}\" width=\"{px}\" height=\"{px}\" alt=\"VARA\" style=\"display:block;border-radius:{radius}px;margin:0 auto\" />";
        }

        // ─── Koristi InlineImageAttachment iz CroMap.Services ─────────────────
        // (nema lokalne InlineImage klase - to je bio uzrok greske)
        private List<InlineImageAttachment> GetLogoAttachments() => new List<InlineImageAttachment>
        {
            new InlineImageAttachment
            {
                ContentId = _logo80Cid,
                Base64Data = _logo80B64,
                MimeType = "image/png",
                FileName = "vara_logo_80.png"
            },
            new InlineImageAttachment
            {
                ContentId = _logo36Cid,
                Base64Data = _logo36B64,
                MimeType = "image/png",
                FileName = "vara_logo_36.png"
            }
        };

        public AuthController(UserRepository repo, IConfiguration configuration, ILogger<AuthController> logger,
            DatabaseConnection dbConnection, IEmailService emailService, PasswordResetRepository resetRepo,
            EmailVerificationRepository verifyRepo, TokenVersionService tokenVersions,
            IProfileRepository profileRepository, IR2StorageService storageService)
        {
            _repo = repo;
            _configuration = configuration;
            _logger = logger;
            _dbConnection = dbConnection;
            _emailService = emailService;
            _resetRepo = resetRepo;
            _verifyRepo = verifyRepo;
            _tokenVersions = tokenVersions;
            _profileRepository = profileRepository;
            _storageService = storageService;
        }

        // ─── Helper za slanje emaila s inline slikama ─────────────────────────
        // Prima List<InlineImageAttachment> (iz Services) - nema vise konflikta tipova
        private async Task SendEmailWithInlineImages(string to, string subject, string htmlBody,
            List<InlineImageAttachment> inlineImages)
        {
            if (_emailService is IEmailServiceWithInlineImages advancedService)
                await advancedService.SendEmailWithInlineImagesAsync(to, subject, htmlBody, inlineImages);
            else
                await _emailService.SendEmailAsync(to, subject, htmlBody);
        }

        [HttpPost("register")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Register([FromBody] UserDto userDto)
        {
            if (string.IsNullOrWhiteSpace(userDto.Username))
                return BadRequest(new { field = "username", code = "required" });
            if (userDto.Username.Length < 3)
                return BadRequest(new { field = "username", code = "minLength" });
            if (string.IsNullOrWhiteSpace(userDto.FirstName))
                return BadRequest(new { field = "firstName", code = "required" });
            if (string.IsNullOrWhiteSpace(userDto.LastName))
                return BadRequest(new { field = "lastName", code = "required" });
            if (string.IsNullOrWhiteSpace(userDto.Password) || userDto.Password.Length < 6)
                return BadRequest(new { field = "password", code = "minLength" });
            if (string.IsNullOrWhiteSpace(userDto.Email))
                return BadRequest(new { field = "email", code = "required" });
            // Format e-maila dosad se uopće nije provjeravao — bilo koji niz
            // znakova prolazio je kao adresa, pa je automatiziranu registraciju
            // ništa nije koštalo: bot nije morao imati ni postojeći sandučić.
            if (!IsValidEmail(userDto.Email))
                return BadRequest(new { field = "email", code = "invalid" });
            // Korisničko ime bez razmaka i posebnih znakova — imena se prikazuju
            // drugim korisnicima i koriste za pretragu.
            if (!IsValidUsername(userDto.Username))
                return BadRequest(new { field = "username", code = "invalid" });
            if (userDto.Username.Length > 30 || userDto.FirstName.Length > 60
                || userDto.LastName.Length > 60 || userDto.Email.Length > 254)
                return BadRequest(new { field = "unknown", code = "tooLong" });
            if (!userDto.BirthDate.HasValue)
                return BadRequest(new { field = "birthDate", code = "required" });

            var email = userDto.Email.Trim();

            // Adresa koju još nitko nije potvrdio nije ničija: da je netko upisao
            // tuđi e-mail, pravi vlasnik ne bi mogao otvoriti račun ("email već
            // postoji"). Takav neprovjereni račun (bez prijave, pa bez sadržaja)
            // se uklanja i registracija ide dalje — nastavit će je samo onaj tko
            // doista dobije kod na tu adresu.
            // Isto i za korisničko ime, ali tek nakon 10 minuta: tko je pogriješio u
            // e-mail adresi (račun mu je ostao neprovjeren) može se ponovno registrirati
            // istim imenom, a tuđa registracija u tijeku se ne može odmah srušiti.
            try
            {
                await _repo.DeleteUnverifiedByEmailAsync(email);
                await _repo.DeleteStaleUnverifiedByUsernameAsync(userDto.Username.Trim());
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Brisanje neprovjerenog računa nije uspjelo"); }

            // 🔥 Provjera jedinstvenosti PRIJE inserta — precizna po polju
            if (await _repo.UsernameExistsAsync(userDto.Username))
                return Conflict(new { field = "username", code = "taken" });

            if (await _repo.EmailExistsAsync(email))
                return Conflict(new { field = "email", code = "taken" });

            var lang = NormalizeLang(userDto.Language);

            var user = new User
            {
                // Trim pri spremanju: validacija radi nad obrezanom vrijednošću,
                // pa bi inače u bazu otišao razmak koji provjera nije vidjela.
                Username = userDto.Username.Trim().ToLower(),
                FirstName = userDto.FirstName.Trim(),
                LastName = userDto.LastName.Trim(),
                Email = email,
                PasswordHash = userDto.Password,
                BirthDate = userDto.BirthDate.Value,
                Language = lang,
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                var newUserId = await _repo.RegisterAsync(user);

                // Račun je neprovjeren dok korisnik ne unese kod poslan na ovu adresu.
                // Ako slanje ne uspije, ekran za potvrdu nudi "pošalji ponovno".
                await SendVerificationCodeAsync(newUserId, email, user.FirstName, lang);

                return Ok(new { message = "Registracija uspješna", needsVerification = true, email });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Registration error");
                // Fallback za race condition (dva zahtjeva istovremeno)
                if (ex.Message.Contains("23505") || ex.Message.Contains("duplicate key"))
                    return Conflict(new { field = "unknown", code = "taken" });
                return BadRequest(new { field = "unknown", code = "serverError" });
            }
        }

        private async Task<bool> SendVerificationCodeAsync(int userId, string email, string firstName, string lang)
        {
            var code = System.Security.Cryptography.RandomNumberGenerator
                .GetInt32(100000, 1000000).ToString();

            // Ne baca iznimku: račun u trenutku poziva već postoji, pa kvar pri slanju
            // ne smije registraciju prikazati kao neuspjelu (ponovni pokušaj bi onda
            // javio "korisničko ime zauzeto"). Korisnik kod može zatražiti ponovno.
            try
            {
                // false = kod za ovog korisnika je već poslan prije manje od minute.
                if (!await _verifyRepo.CreateAsync(userId, code))
                    return false;

                await SendEmailWithInlineImages(
                    email,
                    T(lang, "verifySubject"),
                    BuildResetEmail(firstName, code, lang, "verify"),
                    GetLogoAttachments());
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Verifikacijski e-mail NIJE poslan korisniku {UserId}", userId);
                try { await _verifyRepo.DeleteForUserAsync(userId); } catch { /* već zabilježeno */ }
                return false;
            }
        }

        [HttpPost("verify-email")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Code))
                return BadRequest(new { message = "Email i kod su obavezni" });
            try
            {
                var (userId, isValid) = await _verifyRepo.ValidateAsync(dto.Code.Trim(), dto.Email.Trim());
                if (!isValid)
                    return BadRequest(new { message = "Kod je neispravan ili je istekao" });

                using var conn = _dbConnection.CreateConnection();
                await conn.ExecuteAsync(
                    "UPDATE users SET email_verified = TRUE WHERE id = @Id", new { Id = userId });
                await _verifyRepo.DeleteForUserAsync(userId);

                var user = await conn.QueryFirstOrDefaultAsync<User>(
                    "SELECT first_name AS FirstName, email, language AS Language FROM users WHERE id = @Id",
                    new { Id = userId });
                if (user != null)
                {
                    var lang = NormalizeLang(user.Language);
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await SendEmailWithInlineImages(
                                user.Email,
                                T(lang, "welcomeSubject"),
                                BuildWelcomeEmail(user.FirstName, lang),
                                GetLogoAttachments());
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Welcome email failed, ignoring");
                        }
                    });
                }
                return Ok(new { message = "E-mail potvrđen" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "VerifyEmail error");
                return StatusCode(500, new { message = "Greška pri potvrdi e-maila" });
            }
        }

        [HttpPost("resend-verification")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ResendVerification([FromBody] ForgotPasswordDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Email))
                return BadRequest(new { message = "Email je obavezan" });
            try
            {
                using var conn = _dbConnection.CreateConnection();
                var user = await conn.QueryFirstOrDefaultAsync<User>(
                    "SELECT id, first_name AS FirstName, email, language AS Language FROM users " +
                    "WHERE LOWER(email) = LOWER(@Email) AND email_verified = FALSE",
                    new { Email = dto.Email.Trim() });

                // Isti odgovor postoji li račun ili ne (kao kod forgot-password).
                if (user != null)
                    await SendVerificationCodeAsync(user.Id, user.Email, user.FirstName, NormalizeLang(user.Language));
                return Ok(new { message = "Ako račun čeka potvrdu, kod je poslan" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ResendVerification error");
                return StatusCode(500, new { message = "Greška pri slanju koda" });
            }
        }

        [HttpPost("forgot-password")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Email))
                return BadRequest(new { message = "Email je obavezan" });
            // Trim: razmak na kraju (tipkovnice ga često dodaju nakon
            // autocompletea) inače ne bi našao račun, a odgovor je uvijek 200
            // — korisnik bi čekao e-mail koji nikad nije poslan.
            var email = dto.Email.Trim();
            try
            {
                using var conn = _dbConnection.CreateConnection();
                var user = await conn.QueryFirstOrDefaultAsync<User>(
                    "SELECT id, first_name AS FirstName, email, language AS Language FROM users WHERE LOWER(email) = LOWER(@Email)",
                    new { Email = email });

                // Namjerno isti odgovor postoji li email ili ne (spriječi
                // "user enumeration" — prije je 404 na nepostojeći email
                // otkrivao je li netko registriran s tim emailom).
                if (user != null)
                {
                    var lang = NormalizeLang(user.Language);
                    // RandomNumberGenerator umjesto Random: Random nije
                    // kriptografski generator i njegov se niz može rekonstruirati
                    // iz nekoliko viđenih izlaza (a napadač ih može naručiti
                    // koliko želi tražeći resetiranje za vlastiti račun).
                    var code = System.Security.Cryptography.RandomNumberGenerator
                        .GetInt32(100000, 1000000).ToString();

                    // Kod ide ISKLJUČIVO na adresu spremljenu uz račun (user.Email),
                    // nikad na ono što je klijent poslao.
                    if (await _resetRepo.CreateAsync(user.Id, code))
                    {
                        try
                        {
                            await SendEmailWithInlineImages(
                                user.Email,
                                T(lang, "resetSubject"),
                                BuildResetEmail(user.FirstName, code, lang),
                                GetLogoAttachments()
                            );
                        }
                        catch (Exception mailEx)
                        {
                            // Slanje nije uspjelo: kod koji nitko nije dobio ne
                            // smije blokirati ponovni pokušaj (pauza između slanja).
                            // Uzrok je u logu (EmailService bilježi odgovor pružatelja).
                            _logger.LogError(mailEx, "Reset e-mail NIJE poslan korisniku {UserId}", user.Id);
                            await _resetRepo.DeleteForUserAsync(user.Id);
                            // Korisniku se kaže istina (inače čeka poruku koja nikad
                            // ne stiže). To otkriva da račun postoji, ali samo dok
                            // pošta ne radi — napadač kvar ne može izazvati po želji.
                            return StatusCode(503, new { code = "emailUnavailable" });
                        }
                    }
                }
                return Ok(new { message = "Ako email postoji, kod je poslan" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ForgotPassword error");
                return StatusCode(500, new { message = "Greška pri slanju emaila" });
            }
        }

        /// <summary>
        /// Korak 2 resetiranja: poslužitelj provjerava 6-znamenkasti kod (uz e-mail)
        /// PRIJE nego aplikacija prikaže ekran za novu lozinku. Uspjeh poništava kod
        /// i vraća jednokratnu dozvolu (resetToken) kojom se u koraku 3 mijenja lozinka.
        /// </summary>
        [HttpPost("verify-reset-code")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> VerifyResetCode([FromBody] VerifyResetCodeDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Code))
                return BadRequest(new { message = "Email i kod su obavezni" });
            try
            {
                var (userId, isValid) = await _resetRepo.ValidateAsync(dto.Code.Trim(), dto.Email.Trim());
                if (!isValid)
                    return BadRequest(new { message = "Kod je neispravan ili je istekao" });

                var resetToken = await _resetRepo.IssueTicketAsync(userId);
                return Ok(new { resetToken });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "VerifyResetCode error");
                return StatusCode(500, new { message = "Greška pri provjeri koda" });
            }
        }

        [HttpPost("reset-password")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.NewPassword))
                return BadRequest(new { message = "Nova lozinka je obavezna" });
            if (string.IsNullOrWhiteSpace(dto.Code) && string.IsNullOrWhiteSpace(dto.ResetToken))
                return BadRequest(new { message = "Kod je obavezan" });
            if (string.IsNullOrWhiteSpace(dto.Email))
                return BadRequest(new { message = "Email je obavezan" });
            if (dto.NewPassword.Length < 6)
                return BadRequest(new { message = "Lozinka mora imati najmanje 6 znakova" });
            try
            {
                var email = dto.Email.Trim();

                // Novi tijek: dozvola izdana nakon provjere koda (verify-reset-code).
                // Stariji klijenti šalju sam kod u ovom koraku — i to ostaje podržano.
                var (userId, isValid) = !string.IsNullOrWhiteSpace(dto.ResetToken)
                    ? await _resetRepo.RedeemTicketAsync(dto.ResetToken.Trim(), email)
                    : await _resetRepo.ValidateAsync(dto.Code!.Trim(), email);
                if (!isValid)
                    return BadRequest(new { message = "Kod je neispravan ili je istekao" });

                var hashedPassword = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
                using var conn = _dbConnection.CreateConnection();

                // token_version + 1 poništava SVE ranije izdane tokene (odjava sa
                // svih uređaja): tko je imao pristup računu sa starom lozinkom,
                // nakon reseta ga više nema. E-mail je ovime i potvrđen — kod je
                // stigao samo vlasniku sandučića.
                await conn.ExecuteAsync(@"
                    UPDATE users
                    SET password_hash = @Hash, token_version = token_version + 1, email_verified = TRUE
                    WHERE id = @Id",
                    new { Hash = hashedPassword, Id = userId });
                _tokenVersions.Invalidate(userId);

                await _resetRepo.DeleteForUserAsync(userId);
                await _verifyRepo.DeleteForUserAsync(userId);
                return Ok(new { message = "Lozinka uspje\u0161no promijenjena" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ResetPassword error");
                return StatusCode(500, new { message = "Gre\u0161ka pri resetiranju lozinke" });
            }
        }

        private static readonly HashSet<string> _supportedLangs = new() { "hr", "en", "it", "de", "fr" };

        private static string NormalizeLang(string? lang)
        {
            if (string.IsNullOrWhiteSpace(lang))
                return "hr";
            var code = lang.Split('-', '_')[0].ToLowerInvariant();
            return _supportedLangs.Contains(code) ? code : "hr";
        }

        private static readonly Dictionary<string, Dictionary<string, string>> _mail = new()
        {
            ["hr"] = new()
            {
                ["welcomeSubject"] = "Dobrodošli u VARA! 🗺️",
                ["tagline"] = "Otkrijte svako mjesto",
                ["welcomeGreeting"] = "Dobrodošli, {0}! 👋",
                ["welcomeBody"] = "Vaša registracija je uspješna. Sada možete istraživati najljepša mjesta, pratiti prijatelje i dijeliti svoje avanture diljem Hrvatske i šire.",
                ["bullet1"] = "🏖️ Istražite plaže i nacionalne parkove",
                ["bullet2"] = "🍽️ Pronađite restorane i kafiće",
                ["bullet3"] = "🏛 Otkrijte znamenitosti i skrivena mjesta",
                ["footerNote"] = "Otvorite VARA aplikaciju na svom uređaju za prijavu.",
                ["copyright"] = "Sva prava pridržana.",
                ["resetSubject"] = "VARA - Reset lozinke 🔐",
                ["resetTagline"] = "Sigurnosni kod",
                ["resetGreeting"] = "Reset lozinke za korisnika {0}",
                ["resetBody"] = "Primili smo zahtjev za promjenu lozinke vašeg VARA računa. Upotrijebite kod ispod u aplikaciji:",
                ["resetValid"] = "⏳️ Kod je valjan 1 sat od slanja ovog emaila.",
                ["resetIgnore"] = "🔒 Ako niste zatražili promjenu lozinke, zanemarite ovaj email — vaš račun je siguran.",
                ["verifySubject"] = "VARA - Potvrdite e-mail adresu ✉️",
                ["verifyTagline"] = "Potvrda e-maila",
                ["verifyGreeting"] = "Potvrdite e-mail, {0}",
                ["verifyBody"] = "Hvala na registraciji u VARA aplikaciji. Unesite kod ispod u aplikaciju kako biste potvrdili da ova e-mail adresa pripada vama:",
                ["verifyValid"] = "⏳️ Kod je valjan 24 sata od slanja ovog emaila.",
                ["verifyIgnore"] = "🔒 Ako se niste registrirali u VARA aplikaciji, zanemarite ovaj email.",
            },
            ["en"] = new()
            {
                ["welcomeSubject"] = "Welcome to VARA! 🗺️",
                ["tagline"] = "Discover every place",
                ["welcomeGreeting"] = "Welcome, {0}! 👋",
                ["welcomeBody"] = "Your registration was successful. You can now explore the most beautiful places, follow friends, and share your adventures across Croatia and beyond.",
                ["bullet1"] = "🏖️ Explore beaches and national parks",
                ["bullet2"] = "🍽️ Find restaurants and cafés",
                ["bullet3"] = "🏛 Discover landmarks and hidden gems",
                ["footerNote"] = "Open the VARA app on your device to sign in.",
                ["copyright"] = "All rights reserved.",
                ["resetSubject"] = "VARA - Password Reset 🔐",
                ["resetTagline"] = "Security code",
                ["resetGreeting"] = "Password reset for {0}",
                ["resetBody"] = "We received a request to change the password for your VARA account. Use the code below in the app:",
                ["resetValid"] = "⏳️ The code is valid for 1 hour from when this email was sent.",
                ["resetIgnore"] = "🔒 If you did not request a password change, ignore this email — your account is safe.",
                ["verifySubject"] = "VARA - Confirm your email address ✉️",
                ["verifyTagline"] = "Email confirmation",
                ["verifyGreeting"] = "Confirm your email, {0}",
                ["verifyBody"] = "Thanks for signing up for VARA. Enter the code below in the app to confirm that this email address belongs to you:",
                ["verifyValid"] = "⏳️ The code is valid for 24 hours from when this email was sent.",
                ["verifyIgnore"] = "🔒 If you did not sign up for VARA, ignore this email.",
            },
            ["it"] = new()
            {
                ["welcomeSubject"] = "Benvenuto/a su VARA! 🗺️",
                ["tagline"] = "Scopri ogni luogo",
                ["welcomeGreeting"] = "Benvenuto/a, {0}! 👋",
                ["welcomeBody"] = "La tua registrazione è andata a buon fine. Ora puoi esplorare i luoghi più belli, seguire gli amici e condividere le tue avventure in Croazia e oltre.",
                ["bullet1"] = "🏖️ Esplora spiagge e parchi nazionali",
                ["bullet2"] = "🍽️ Trova ristoranti e caffetterie",
                ["bullet3"] = "🏛 Scopri monumenti e luoghi nascosti",
                ["footerNote"] = "Apri l'app VARA sul tuo dispositivo per accedere.",
                ["copyright"] = "Tutti i diritti riservati.",
                ["resetSubject"] = "VARA - Reimposta password 🔐",
                ["resetTagline"] = "Codice di sicurezza",
                ["resetGreeting"] = "Reimpostazione password per {0}",
                ["resetBody"] = "Abbiamo ricevuto una richiesta di modifica della password del tuo account VARA. Usa il codice qui sotto nell'app:",
                ["resetValid"] = "⏳️ Il codice è valido per 1 ora dall'invio di questa email.",
                ["resetIgnore"] = "🔒 Se non hai richiesto una modifica della password, ignora questa email — il tuo account è al sicuro.",
                ["verifySubject"] = "VARA - Conferma il tuo indirizzo e-mail ✉️",
                ["verifyTagline"] = "Conferma e-mail",
                ["verifyGreeting"] = "Conferma la tua e-mail, {0}",
                ["verifyBody"] = "Grazie per esserti registrato/a su VARA. Inserisci il codice qui sotto nell'app per confermare che questo indirizzo e-mail ti appartiene:",
                ["verifyValid"] = "⏳️ Il codice è valido per 24 ore dall'invio di questa email.",
                ["verifyIgnore"] = "🔒 Se non ti sei registrato/a su VARA, ignora questa email.",
            },
            ["de"] = new()
            {
                ["welcomeSubject"] = "Willkommen bei VARA! 🗺️",
                ["tagline"] = "Entdecke jeden Ort",
                ["welcomeGreeting"] = "Willkommen, {0}! 👋",
                ["welcomeBody"] = "Ihre Registrierung war erfolgreich. Jetzt können Sie die schönsten Orte erkunden, Freunden folgen und Ihre Abenteuer in ganz Kroatien und darüber hinaus teilen.",
                ["bullet1"] = "🏖️ Entdecken Sie Strände und Nationalparks",
                ["bullet2"] = "🍽️ Finden Sie Restaurants und Cafés",
                ["bullet3"] = "🏛 Entdecken Sie Sehenswürdigkeiten und versteckte Orte",
                ["footerNote"] = "Öffnen Sie die VARA-App auf Ihrem Gerät, um sich anzumelden.",
                ["copyright"] = "Alle Rechte vorbehalten.",
                ["resetSubject"] = "VARA - Passwort zurücksetzen 🔐",
                ["resetTagline"] = "Sicherheitscode",
                ["resetGreeting"] = "Passwort zurücksetzen für {0}",
                ["resetBody"] = "Wir haben eine Anfrage zur Änderung des Passworts Ihres VARA-Kontos erhalten. Verwenden Sie den folgenden Code in der App:",
                ["resetValid"] = "⏳️ Der Code ist 1 Stunde ab dem Versand dieser E-Mail gültig.",
                ["resetIgnore"] = "🔒 Falls Sie keine Passwortänderung angefordert haben, ignorieren Sie diese E-Mail — Ihr Konto ist sicher.",
                ["verifySubject"] = "VARA - Bestätigen Sie Ihre E-Mail-Adresse ✉️",
                ["verifyTagline"] = "E-Mail-Bestätigung",
                ["verifyGreeting"] = "Bestätigen Sie Ihre E-Mail, {0}",
                ["verifyBody"] = "Danke für Ihre Registrierung bei VARA. Geben Sie den folgenden Code in der App ein, um zu bestätigen, dass diese E-Mail-Adresse Ihnen gehört:",
                ["verifyValid"] = "⏳️ Der Code ist 24 Stunden ab dem Versand dieser E-Mail gültig.",
                ["verifyIgnore"] = "🔒 Falls Sie sich nicht bei VARA registriert haben, ignorieren Sie diese E-Mail.",
            },
            ["fr"] = new()
            {
                ["welcomeSubject"] = "Bienvenue sur VARA ! 🗺️",
                ["tagline"] = "Découvrez chaque lieu",
                ["welcomeGreeting"] = "Bienvenue, {0} ! 👋",
                ["welcomeBody"] = "Votre inscription a réussi. Vous pouvez désormais explorer les plus beaux lieux, suivre vos amis et partager vos aventures à travers la Croatie et au-delà.",
                ["bullet1"] = "🏖️ Explorez les plages et les parcs nationaux",
                ["bullet2"] = "🍽️ Trouvez des restaurants et des cafés",
                ["bullet3"] = "🏛 Découvrez des monuments et des lieux cachés",
                ["footerNote"] = "Ouvrez l'application VARA sur votre appareil pour vous connecter.",
                ["copyright"] = "Tous droits réservés.",
                ["resetSubject"] = "VARA - Réinitialisation du mot de passe 🔐",
                ["resetTagline"] = "Code de sécurité",
                ["resetGreeting"] = "Réinitialisation du mot de passe pour {0}",
                ["resetBody"] = "Nous avons reçu une demande de modification du mot de passe de votre compte VARA. Utilisez le code ci-dessous dans l'application :",
                ["resetValid"] = "⏳️ Le code est valable 1 heure à compter de l'envoi de cet e-mail.",
                ["resetIgnore"] = "🔒 Si vous n'avez pas demandé de modification de mot de passe, ignorez cet e-mail — votre compte est en sécurité.",
                ["verifySubject"] = "VARA - Confirmez votre adresse e-mail ✉️",
                ["verifyTagline"] = "Confirmation de l'e-mail",
                ["verifyGreeting"] = "Confirmez votre e-mail, {0}",
                ["verifyBody"] = "Merci de vous être inscrit(e) sur VARA. Saisissez le code ci-dessous dans l'application pour confirmer que cette adresse e-mail vous appartient :",
                ["verifyValid"] = "⏳️ Le code est valable 24 heures à compter de l'envoi de cet e-mail.",
                ["verifyIgnore"] = "🔒 Si vous ne vous êtes pas inscrit(e) sur VARA, ignorez cet e-mail.",
            },
        };

        private static string T(string lang, string key) => _mail[NormalizeLang(lang)][key];

        private string BuildWelcomeEmail(string firstName, string language)
        {
            var lang = NormalizeLang(language);
            return $@"
<!DOCTYPE html>
<html>
<head><meta charset='UTF-8'><meta name='viewport' content='width=device-width, initial-scale=1.0'></head>
<body style='font-family:Arial,sans-serif;background:#1B3F0E;padding:20px;margin:0'>
  <div style='max-width:520px;margin:0 auto;background:#ffffff;border-radius:20px;overflow:hidden;box-shadow:0 8px 32px rgba(0,0,0,0.35)'>
    <div style='background:linear-gradient(160deg,#2D6418 0%,#142F09 100%);padding:36px 32px 28px;text-align:center'>
      <table cellpadding='0' cellspacing='0' border='0' width='100%'>
        <tr><td align='center' style='padding-bottom:12px'>{LogoImg(_logo80Cid, 80)}</td></tr>
        <tr><td align='center'><div style='color:#ffffff;font-size:34px;font-weight:900;letter-spacing:12px;padding-left:12px'>VARA</div></td></tr>
        <tr><td align='center'><div style='color:rgba(200,225,200,0.6);font-size:12px;letter-spacing:3px;text-transform:uppercase;padding-top:4px'>{T(lang, "tagline")}</div></td></tr>
      </table>
    </div>
    <div style='padding:32px'>
      <h2 style='color:#1a1a1a;font-size:22px;margin:0 0 12px;font-weight:800'>{string.Format(T(lang, "welcomeGreeting"), firstName)}</h2>
      <p style='color:#555;line-height:1.7;font-size:15px;margin:0 0 24px'>{T(lang, "welcomeBody")}</p>
      <div style='background:#f0f7ee;border-radius:14px;padding:20px;margin-bottom:24px'>
        <div style='margin-bottom:10px'><span style='color:#2D6418;font-weight:700;font-size:14px'>{T(lang, "bullet1")}</span></div>
        <div style='margin-bottom:10px'><span style='color:#2D6418;font-weight:700;font-size:14px'>{T(lang, "bullet2")}</span></div>
        <div style='margin-bottom:0'><span style='color:#2D6418;font-weight:700;font-size:14px'>{T(lang, "bullet3")}</span></div>
      </div>
      <p style='color:#888;font-size:13px;text-align:center;margin:0 0 8px'>{T(lang, "footerNote")}</p>
    </div>
    <div style='background:#f8f8f8;border-top:1px solid #e8e8e8;padding:16px 32px'>
      <table cellpadding='0' cellspacing='0' border='0' width='100%'>
        <tr><td align='center' style='padding-bottom:8px'>{LogoImg(_logo36Cid, 36)}</td></tr>
        <tr><td align='center'><span style='color:#aaa;font-size:12px'>&#169; {DateTime.Now.Year} VARA. {T(lang, "copyright")}</span></td></tr>
      </table>
    </div>
  </div>
</body>
</html>";
        }

        private string BuildResetEmail(string firstName, string code, string language, string keyPrefix = "reset")
        {
            var lang = NormalizeLang(language);
            return $@"
<!DOCTYPE html>
<html>
<head>
<meta charset='UTF-8'>
<meta name='viewport' content='width=device-width, initial-scale=1.0'>
<style>
  @media only screen and (max-width: 420px) {{
    .vara-content {{ padding: 20px !important; }}
    .vara-code-box {{ padding: 14px 6px !important; }}
    .vara-code-cell {{ width: 26px !important; min-width: 26px !important; font-size: 22px !important; padding: 6px 1px !important; }}
  }}
</style>
</head>
<body style='font-family:Arial,sans-serif;background:#1B3F0E;padding:20px;margin:0'>
  <div style='max-width:520px;margin:0 auto;background:#ffffff;border-radius:20px;overflow:hidden;box-shadow:0 8px 32px rgba(0,0,0,0.35)'>
    <div style='background:linear-gradient(160deg,#2D6418 0%,#142F09 100%);padding:36px 32px 28px;text-align:center'>
      <table cellpadding='0' cellspacing='0' border='0' width='100%'>
        <tr><td align='center' style='padding-bottom:12px'>{LogoImg(_logo80Cid, 80)}</td></tr>
        <tr><td align='center'><div style='color:#ffffff;font-size:34px;font-weight:900;letter-spacing:12px;padding-left:12px'>VARA</div></td></tr>
        <tr><td align='center'><div style='color:rgba(200,225,200,0.6);font-size:12px;letter-spacing:3px;text-transform:uppercase;padding-top:4px'>{T(lang, keyPrefix + "Tagline")}</div></td></tr>
      </table>
    </div>
    <div class='vara-content' style='padding:32px'>
      <h2 style='color:#1a1a1a;font-size:20px;margin:0 0 8px;font-weight:800'>{string.Format(T(lang, keyPrefix + "Greeting"), firstName)}</h2>
      <p style='color:#555;line-height:1.7;font-size:15px;margin:0 0 28px'>{T(lang, keyPrefix + "Body")}</p>
      <table cellpadding='0' cellspacing='0' border='0' width='100%' style='margin-bottom:28px'>
        <tr><td align='center' valign='middle'>
          <table cellpadding='0' cellspacing='0' border='0' style='margin:0 auto;max-width:100%'>
            <tr><td class='vara-code-box' align='center' valign='middle' style='background:linear-gradient(135deg,#2D6418,#142F09);border-radius:14px;padding:16px 10px'>
              <table cellpadding='0' cellspacing='0' border='0' style='margin:0 auto'>
                <tr>
                  {string.Join("", code.Select(c => $"<td class='vara-code-cell' width='30' align='center' valign='middle' style='width:30px;min-width:30px;color:#ffffff;font-size:28px;font-weight:900;line-height:1;font-family:Courier New,Courier,monospace;text-align:center;padding:6px 2px'>{c}</td>"))}
                </tr>
              </table>
            </td></tr>
          </table>
        </td></tr>
      </table>
      <div style='background:#fff8e1;border:1px solid #ffe082;border-radius:12px;padding:16px'>
        <p style='color:#795548;font-size:13px;margin:0;line-height:1.6'>
          {T(lang, keyPrefix + "Valid")}<br>
          {T(lang, keyPrefix + "Ignore")}
        </p>
      </div>
    </div>
    <div style='background:#f8f8f8;border-top:1px solid #e8e8e8;padding:16px 32px'>
      <table cellpadding='0' cellspacing='0' border='0' width='100%'>
        <tr><td align='center' style='padding-bottom:8px'>{LogoImg(_logo36Cid, 36)}</td></tr>
        <tr><td align='center'><span style='color:#aaa;font-size:12px'>&#169; {DateTime.Now.Year} VARA. {T(lang, "copyright")}</span></td></tr>
      </table>
    </div>
  </div>
</body>
</html>";
        }



        [HttpPost("login")]
        [EnableRateLimiting("auth")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            try
            {
                _logger.LogInformation($"Login attempt: {dto.Username}");
                var user = await _repo.LoginByUsernameAsync(dto.Username, dto.Password);
                if (user == null)
                {
                    _logger.LogWarning($"Login failed for: {dto.Username}");
                    return Unauthorized(new { message = "Neispravno korisni\u010dko ime ili lozinka" });
                }
                // Lozinka je točna, ali e-mail nije potvrđen: bez ulaska, aplikacija
                // vodi na ekran za unos koda.
                if (!user.EmailVerified)
                    return StatusCode(403, new
                    {
                        code = "emailNotVerified",
                        email = user.Email,
                        message = "E-mail adresa nije potvrđena"
                    });
                _logger.LogInformation($"Login successful: {user.Username} (ID: {user.Id})");
                var token = await GenerateJwtTokenAsync(user);
                return Ok(new
                {
                    token = token,
                    userId = user.Id,
                    username = user.Username,
                    firstName = user.FirstName,
                    lastName = user.LastName,
                    email = user.Email,
                    phone = user.Phone
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login error");
                return StatusCode(500, new { message = "Gre\u0161ka na serveru" });
            }
        }



        // Ranije bez [Authorize] — cijeli popis korisnika (ime, prezime,
        // korisničko ime) mogao je scrapati bilo tko bez prijave.
        [Authorize]
        [HttpGet("users")]
        public async Task<IActionResult> GetUsers()
        {
            try
            {
                // STARI sql vraćao je p.avatar AS Avatar (veliko A)
                // Dapper dynamic vraća lowercase ključeve na nekim DB driverima
                // Eksplicitno vraćamo lowercase da frontend može čitati
                var sql = @"
            SELECT 
                u.id          AS id, 
                u.first_name  AS firstname, 
                u.last_name   AS lastname, 
                u.username    AS username, 
                COALESCE(p.avatar, '')  AS avatar
            FROM users u 
            LEFT JOIN user_profiles p ON u.id = p.user_id
            ORDER BY u.first_name, u.last_name";

                using var connection = _dbConnection.CreateConnection();
                var users = await connection.QueryAsync(sql);
                return Ok(users);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting users");
                return StatusCode(500, new { message = "Greška pri dohvaćanju korisnika" });
            }
        }

        // Ranije bez [Authorize] — dopuštalo je anonimno nabrajanje
        // korisničkih ID-eva (1, 2, 3, ...) i njihovih imena/korisničkih
        // imena bez ikakve prijave.
        [Authorize]
        [HttpGet("users/{id}")]
        public async Task<IActionResult> GetUser(int id)
        {
            try
            {
                var user = await _repo.GetUserByIdAsync(id);
                if (user == null)
                    return NotFound(new { message = "Korisnik nije prona\u0111en" });
                var avatar = await _repo.GetUserAvatarAsync(id);
                return Ok(new { user.Id, user.FirstName, user.LastName, user.Username, Avatar = avatar });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting user {id}");
                return StatusCode(500, new { message = "Gre\u0161ka pri dohva\u0107anju korisnika" });
            }
        }

        // Ranije bez [Authorize] i bez provjere vlasništva — bilo tko je
        // mogao poslati PUT s proizvoljnim ID-em i prepisati ime, korisničko
        // ime pa i EMAIL bilo kojeg korisnika (npr. na svoj email, pa potom
        // resetirati lozinku putem "zaboravljene lozinke" — potpuno
        // preuzimanje tuđeg računa bez ijedne lozinke).
        [Authorize]
        [HttpPut("users/{id}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] UserDto userDto)
        {
            try
            {
                var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
                if (currentUserId != id)
                    return Forbid();

                if (string.IsNullOrWhiteSpace(userDto.FirstName))
                    return BadRequest(new { message = "Ime je obavezno" });
                if (string.IsNullOrWhiteSpace(userDto.LastName))
                    return BadRequest(new { message = "Prezime je obavezno" });
                if (!userDto.BirthDate.HasValue)
                    return BadRequest(new { message = "Datum ro\u0111enja je obavezan" });
                var user = new User
                {
                    Id = id,
                    Username = userDto.Username,
                    FirstName = userDto.FirstName,
                    LastName = userDto.LastName,
                    Email = userDto.Email,
                    BirthDate = userDto.BirthDate.Value,
                    CreatedAt = DateTime.UtcNow
                };
                await _repo.UpdateUserAsync(user);
                return Ok(new { message = "Korisnik a\u017euriran" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating user {id}");
                return StatusCode(500, new { message = "Gre\u0161ka pri a\u017euriranju korisnika" });
            }
        }

        [HttpPost("google")]
        public async Task<IActionResult> GoogleAuth([FromBody] GoogleAuthDto dto)
        {
            GoogleJsonWebSignature.Payload payload;
            try
            {
                payload = await GoogleJsonWebSignature.ValidateAsync(dto.IdToken, new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { _configuration["Google:WebClientId"] }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Invalid Google token");
                return Unauthorized(new { message = "Nevažeći Google token" });
            }

            try
            {
                using var connection = _dbConnection.CreateConnection();

                // LOWER() na oba stupca je nužan — email se kod obične
                // registracije sprema točno kako je korisnik utipkao (bez
                // lowercasinga), a Google uvijek vraća email u lowercase
                // obliku. Bez ovoga bi "Karmela@Gmail.com" (ručna
                // registracija) i "karmela@gmail.com" (Google) bili
                // tretirani kao DVA različita korisnika — što je točno
                // stvaralo duple račune (isti čovjek s dva različita
                // korisnička imena, npr. "karmela" i "karmela0123").
                var existingUser = await connection.QueryFirstOrDefaultAsync<User>(@"
            SELECT
                id, username, first_name AS FirstName, last_name AS LastName,
                email, phone, password_hash AS PasswordHash, birth_date AS BirthDate,
                created_at AS CreatedAt, is_admin AS IsAdmin, google_id AS GoogleId,
                auth_provider AS AuthProvider, email_verified AS EmailVerified
            FROM users
            WHERE google_id = @GoogleId OR LOWER(email) = LOWER(@Email)",
                    new { GoogleId = payload.Subject, Email = payload.Email });

                User user;
                bool needsBirthDate;
                bool needsPassword;

                if (existingUser != null)
                {
                    user = existingUser;
                    needsBirthDate = !user.BirthDate.HasValue;
                    needsPassword = string.IsNullOrEmpty(user.PasswordHash); // 🔥 ključna provjera

                    if (string.IsNullOrEmpty(user.GoogleId))
                    {
                        await connection.ExecuteAsync(
                            "UPDATE users SET google_id = @GoogleId, auth_provider = 'google' WHERE id = @Id",
                            new { GoogleId = payload.Subject, user.Id });
                    }

                    // Google je dokazao da je ovaj e-mail kod prijavljenog korisnika. Ako je
                    // račun s tom adresom bio neprovjeren, lozinku je mogao postaviti netko
                    // tko je tuđu adresu samo upisao pri registraciji — pa se briše (korisnik
                    // postavlja novu), a sve ranije sesije se poništavaju.
                    if (payload.EmailVerified && !user.EmailVerified)
                    {
                        await connection.ExecuteAsync(@"
                            UPDATE users
                            SET email_verified = TRUE, password_hash = NULL, token_version = token_version + 1
                            WHERE id = @Id", new { user.Id });
                        _tokenVersions.Invalidate(user.Id);
                        needsPassword = true;
                    }
                }
                else
                {
                    var baseUsername = payload.Email.Split('@')[0].ToLower();
                    var username = baseUsername;
                    var suffix = 1;
                    while (await connection.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM users WHERE username = @Username", new { Username = username }) > 0)
                    {
                        username = $"{baseUsername}{suffix++}";
                    }

                    var newUserId = await connection.ExecuteScalarAsync<int>(@"
    INSERT INTO users (first_name, last_name, email, username, password_hash, google_id, auth_provider, birth_date, language, email_verified, created_at)
    VALUES (@FirstName, @LastName, @Email, @Username, NULL, @GoogleId, 'google', NULL, @Language, @EmailVerified, @CreatedAt)
    RETURNING id",
    new
    {
        FirstName = payload.GivenName ?? "Korisnik",
        LastName = payload.FamilyName ?? "",
        Email = payload.Email,
        Username = username,
        GoogleId = payload.Subject,
        Language = NormalizeLang(Request.Headers["Accept-Language"].FirstOrDefault()),
        EmailVerified = payload.EmailVerified,
        CreatedAt = DateTime.UtcNow
    });

                    user = await connection.QueryFirstAsync<User>(@"
                SELECT 
                    id, username, first_name AS FirstName, last_name AS LastName,
                    email, phone, password_hash AS PasswordHash, birth_date AS BirthDate,
                    created_at AS CreatedAt, is_admin AS IsAdmin, google_id AS GoogleId,
                    auth_provider AS AuthProvider
                FROM users WHERE id = @Id", new { Id = newUserId });
                    needsBirthDate = true;
                    needsPassword = true; // 🔥 novi Google korisnik nema lozinku
                }

                var token = await GenerateJwtTokenAsync(user);
                return Ok(new
                {
                    token,
                    userId = user.Id,
                    username = user.Username,
                    firstName = user.FirstName,
                    lastName = user.LastName,
                    email = user.Email,
                    needsBirthDate,
                    needsPassword // 🔥 novo polje
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GoogleAuth error");
                return StatusCode(500, new { message = "Greška pri Google prijavi" });
            }
        }
        public class GoogleAuthDto
        {
            public string IdToken { get; set; } = string.Empty;
            public string? Language { get; set; }
        }


        [HttpPut("complete-profile")]
        [Authorize]
        public async Task<IActionResult> CompleteProfile([FromBody] CompleteProfileDto dto)
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                return Unauthorized();

            using var connection = _dbConnection.CreateConnection();
            await connection.ExecuteAsync(
                "UPDATE users SET birth_date = @BirthDate WHERE id = @Id",
                new { BirthDate = dto.BirthDate.ToDateTime(TimeOnly.MinValue), Id = userId });

            return Ok(new { message = "Profil dovršen" });
        }

        public class CompleteProfileDto
        {
            public DateOnly BirthDate { get; set; }
        }

        [HttpDelete("users/{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            try
            {
                await _repo.DeleteUserAsync(id);
                return Ok(new { message = "Korisnik obrisan" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting user {id}");
                return StatusCode(500, new { message = "Gre\u0161ka pri brisanju korisnika" });
            }
        }

        /// <summary>
        /// Osnovna provjera formata e-mail adrese. Namjerno konzervativna:
        /// treba odsjeći očite smeće-vrijednosti, ne biti potpuna RFC 5322
        /// implementacija.
        /// </summary>
        private static bool IsValidEmail(string email)
        {
            email = email.Trim();
            if (email.Length < 5 || email.Length > 254) return false;
            if (email.Contains(' ') || email.Contains("..")) return false;

            var at = email.IndexOf('@');
            if (at <= 0 || at != email.LastIndexOf('@')) return false;
            if (at == email.Length - 1) return false;

            var local = email[..at];
            var domain = email[(at + 1)..];
            if (local.StartsWith('.') || local.EndsWith('.')) return false;

            var dot = domain.LastIndexOf('.');
            if (dot <= 0 || dot == domain.Length - 1) return false;
            if (domain.StartsWith('.') || domain.StartsWith('-')) return false;

            // Vršna domena: samo slova, barem dva znaka.
            var tld = domain[(dot + 1)..];
            if (tld.Length < 2) return false;
            foreach (var c in tld)
                if (!char.IsLetter(c)) return false;

            foreach (var c in domain)
                if (!char.IsLetterOrDigit(c) && c != '.' && c != '-') return false;

            return true;
        }

        /// <summary>Slova, brojke, točka, donja crta i crtica; 3–30 znakova.</summary>
        private static bool IsValidUsername(string username)
        {
            username = username.Trim();
            if (username.Length < 3 || username.Length > 30) return false;
            foreach (var c in username)
                if (!char.IsLetterOrDigit(c) && c != '.' && c != '_' && c != '-')
                    return false;
            return true;
        }

        private async Task<string> GenerateJwtTokenAsync(User user)
        {
            // Verzija sesije ide u token: reset lozinke je povećava i time poništava
            // sve ranije izdane tokene (provjera u Program.cs, OnTokenValidated).
            using var vconn = _dbConnection.CreateConnection();
            var tokenVersion = await vconn.ExecuteScalarAsync<int>(
                "SELECT token_version FROM users WHERE id = @Id", new { user.Id });

            var tokenHandler = new JwtSecurityTokenHandler();
            // UTF8, jednako kao pri provjeri tokena u Program.cs. ASCII bi svaki
            // znak izvan ASCII-ja u ključu pretvorio u "?", pa bi potpisivanje i
            // provjera koristili različite bajtove.
            var key = Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!);
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, $"{user.FirstName} {user.LastName}"),
                new Claim("firstName", user.FirstName ?? ""),
                new Claim("lastName", user.LastName ?? ""),
                new Claim("tv", tokenVersion.ToString())
            };
            if (!string.IsNullOrEmpty(user.Email))
                claims.Add(new Claim(ClaimTypes.Email, user.Email));
            if (!string.IsNullOrEmpty(user.Phone))
                claims.Add(new Claim("phone", user.Phone));
            if (user.IsAdmin)
            {
                claims.Add(new Claim(ClaimTypes.Role, "Admin"));
                claims.Add(new Claim("isAdmin", "true"));
            }
            else
                claims.Add(new Claim(ClaimTypes.Role, "User"));

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddDays(7),
                Issuer = _configuration["Jwt:Issuer"],
                Audience = _configuration["Jwt:Audience"],
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        /// <summary>
        /// Je li prijava još valjana. Sam [Authorize] (uključujući provjeru verzije
        /// sesije u Program.cs) odlučuje: poništen token dobije 401. Aplikacija ga
        /// zove pri povratku u prvi plan da se korisnik odjavljen resetom lozinke
        /// na drugom uređaju odmah vrati na prijavu, umjesto na prazne ekrane.
        /// </summary>
        [HttpGet("session")]
        [Authorize]
        public IActionResult Session() => NoContent();

        /// <summary>
        /// Trajno briše račun prijavljenog korisnika, sa svim njegovim sadržajem.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Aplikacija je ovu rutu pozivala otprije (gumb "Obriši račun" u profilu),
        /// ali rute nije bilo. Poziv je vraćao 404, odgovor se nije provjeravao, a
        /// aplikacija bi svejedno očistila pohranu i odvela korisnika na prijavu.
        /// Korisnik je time bio uvjeren da je račun obrisan, dok su račun, objave,
        /// fotografije i poruke ostajali na poslužitelju — mogao se ponovno
        /// prijaviti i sve zateći.
        /// </para>
        /// <para>
        /// Korisnik se uzima ISKLJUČIVO iz tokena, nikad iz tijela zahtjeva —
        /// inače bi bilo tko mogao obrisati tuđi račun.
        /// </para>
        /// <para>
        /// Redoslijed je namjeran: prvo se popišu datoteke, zatim se briše redak u
        /// bazi (ostalo ide kaskadno), pa tek onda datoteke iz pohrane. Ako
        /// brisanje datoteke padne, račun je ipak obrisan — zaostala datoteka u
        /// kanti je manje zlo od računa koji je ostao pola obrisan.
        /// </para>
        /// </remarks>
        [HttpDelete("delete-account")]
        [Authorize]
        public async Task<IActionResult> DeleteAccount()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                return Unauthorized();

            using var connection = _dbConnection.CreateConnection();

            // Putanje do medija treba pokupiti PRIJE brisanja — nakon kaskade
            // više nema iz čega.
            var mediaPaths = new List<string>();
            try
            {
                mediaPaths.AddRange(await connection.QueryAsync<string>(
                    """
                    SELECT avatar      FROM user_profiles WHERE user_id = @UserId AND avatar      IS NOT NULL AND avatar      <> ''
                    UNION ALL
                    SELECT file_path      FROM videos        WHERE user_id = @UserId AND file_path      IS NOT NULL AND file_path      <> ''
                    UNION ALL
                    SELECT thumbnail_path FROM videos        WHERE user_id = @UserId AND thumbnail_path IS NOT NULL AND thumbnail_path <> ''
                    UNION ALL
                    SELECT media_url      FROM stories       WHERE user_id = @UserId AND media_url      IS NOT NULL AND media_url      <> ''
                    """,
                    new { UserId = userId }));
            }
            catch (Exception ex)
            {
                // Popis medija ne smije spriječiti brisanje računa.
                _logger.LogWarning(ex, "Popis medija za korisnika {UserId} nije uspio.", userId);
            }

            var deleted = await _profileRepository.DeleteAccountAsync(userId);
            if (!deleted)
                return NotFound(new { message = "Račun nije pronađen." });

            _logger.LogInformation(
                "Račun {UserId} obrisan; datoteka za brisanje: {Count}.", userId, mediaPaths.Count);

            foreach (var path in mediaPaths.Distinct())
            {
                try
                {
                    await _storageService.DeleteFileAsync(path);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Datoteka {Path} nije obrisana iz pohrane.", path);
                }
            }

            return Ok(new { message = "Račun je obrisan." });
        }

        [HttpPut("language")]
        [Authorize]
        public async Task<IActionResult> UpdateLanguage([FromBody] UpdateLanguageDto dto)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                return Unauthorized();

            var lang = NormalizeLang(dto.Language);
            using var connection = _dbConnection.CreateConnection();
            await connection.ExecuteAsync(
                "UPDATE users SET language = @Language WHERE id = @Id",
                new { Language = lang, Id = userId });

            return Ok(new { message = "Jezik ažuriran" });
        }

        public class UpdateLanguageDto
        {
            public string Language { get; set; } = string.Empty;
        }

        [HttpPut("set-password")]
        [Authorize]
        public async Task<IActionResult> SetPassword([FromBody] SetPasswordDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 6)
                return BadRequest(new { message = "Lozinka mora imati najmanje 6 znakova" });

            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                return Unauthorized();

            try
            {
                var hashedPassword = BCrypt.Net.BCrypt.HashPassword(dto.Password);
                using var connection = _dbConnection.CreateConnection();
                await connection.ExecuteAsync(
                    "UPDATE users SET password_hash = @Hash WHERE id = @Id",
                    new { Hash = hashedPassword, Id = userId });

                return Ok(new { message = "Lozinka postavljena" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SetPassword error");
                return StatusCode(500, new { message = "Greška pri postavljanju lozinke" });
            }
        }

        public class SetPasswordDto
        {
            public string Password { get; set; } = string.Empty;
        }
    }
}