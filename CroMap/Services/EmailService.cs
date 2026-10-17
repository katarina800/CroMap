using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace CroMap.Services
{
    public class EmailService : IEmailServiceWithInlineImages
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

        // Jedan HttpClient za sve zahtjeve (ne stvara nove utičnice pri svakom slanju);
        // kratak timeout da korisnik ne čeka minutama kad pružatelj ne odgovara.
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

        public EmailService(IConfiguration config, ILogger<EmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendEmailAsync(string to, string subject, string htmlBody)
        {
            await SendEmailWithInlineImagesAsync(to, subject, htmlBody, new List<InlineImageAttachment>());
        }

        public async Task SendEmailWithInlineImagesAsync(
            string to, string subject, string htmlBody,
            List<InlineImageAttachment> inlineImages)
        {
            // Pružatelji, redom: Brevo (HTTPS API), SendGrid (HTTPS API), SMTP.
            // Prvi koji uspije šalje poruku; ako padne, pokušava se sljedeći.
            //
            // Preporuka za Render: koristi API (Brevo__ApiKey ili SendGrid__ApiKey).
            // Besplatne Render instance blokiraju izlazni SMTP (portovi 25/465/587),
            // pa SMTP tamo završi s "Connection timed out".
            var providers = new List<(string Name, Func<Task> Send)>();

            var brevoKey = _config["Brevo__ApiKey"] ?? _config["Brevo:ApiKey"] ?? "";
            if (!string.IsNullOrWhiteSpace(brevoKey))
                providers.Add(("Brevo", () => SendViaBrevoAsync(brevoKey, to, subject, htmlBody, inlineImages)));

            var sendGridKey = _config["SendGrid__ApiKey"] ?? _config["SendGrid:ApiKey"] ?? "";
            if (!string.IsNullOrWhiteSpace(sendGridKey))
                providers.Add(("SendGrid", () => SendViaSendGridAsync(sendGridKey, to, subject, htmlBody, inlineImages)));

            var smtpHost = _config["Smtp__Host"] ?? _config["Smtp:Host"] ?? "";
            if (!string.IsNullOrWhiteSpace(smtpHost))
                providers.Add(("SMTP", () => SendViaSmtpAsync(to, subject, htmlBody, inlineImages)));

            if (providers.Count == 0)
                throw new InvalidOperationException(
                    "Nije postavljen nijedan pružatelj e-pošte (Brevo__ApiKey, SendGrid__ApiKey ili Smtp__Host).");

            Exception? last = null;
            foreach (var (name, send) in providers)
            {
                try
                {
                    await send();
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    _logger.LogWarning(ex, "{Provider} nije poslao poruku.", name);
                }
            }

            throw new Exception("Nijedan pružatelj e-pošte nije poslao poruku.", last);
        }

        /// <summary>
        /// Brevo transakcijski API (HTTPS, port 443 — radi i na besplatnom Renderu).
        /// Pošiljatelj (Email__From) MORA biti potvrđen u Brevu (Senders, domains, IPs).
        /// Brevo ne podržava CID slike u tijelu poruke, pa se "cid:..." zamjenjuje
        /// javnim adresama logotipa s ovog poslužitelja (wwwroot/images).
        /// </summary>
        private async Task SendViaBrevoAsync(
            string apiKey, string to, string subject, string htmlBody, List<InlineImageAttachment> inlineImages)
        {
            var fromEmail = _config["Email__From"] ?? _config["Email:From"] ?? "";
            var fromName = _config["Email__FromName"] ?? _config["Email:FromName"] ?? "VARA";
            if (string.IsNullOrWhiteSpace(fromEmail))
                throw new InvalidOperationException(
                    "Email__From nije postavljen — mora biti pošiljatelj potvrđen u Brevu.");

            var baseUrl = (_config["Email__PublicBaseUrl"] ?? _config["Email:PublicBaseUrl"]
                           ?? "https://cromap.onrender.com").TrimEnd('/');

            var html = htmlBody;
            foreach (var img in inlineImages)
            {
                var file = img.ContentId switch
                {
                    "vara_logo_80" => "vara_email_80.png",
                    "vara_logo_36" => "vara_email_36.png",
                    _ => null
                };
                if (file != null)
                    html = html.Replace("cid:" + img.ContentId, $"{baseUrl}/images/{file}");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
            request.Headers.Add("api-key", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = JsonContent.Create(new
            {
                sender = new { name = fromName, email = fromEmail },
                to = new[] { new { email = to } },
                subject,
                htmlContent = html
            });

            using var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError("Brevo error {Status}: {Body}", (int)response.StatusCode, body);
                throw new Exception($"Brevo failed: {(int)response.StatusCode}");
            }

            _logger.LogInformation("Email sent via Brevo to {To}", to);
        }

        private async Task SendViaSmtpAsync(
            string to, string subject, string htmlBody, List<InlineImageAttachment> inlineImages)
        {
            var host = _config["Smtp__Host"] ?? _config["Smtp:Host"] ?? "";
            var port = int.TryParse(_config["Smtp__Port"] ?? _config["Smtp:Port"], out var p) ? p : 587;
            var user = _config["Smtp__User"] ?? _config["Smtp:User"] ?? "";
            var pass = _config["Smtp__Password"] ?? _config["Smtp:Password"] ?? "";
            var fromEmail = _config["Email__From"] ?? _config["Email:From"] ?? user;
            var fromName = _config["Email__FromName"] ?? _config["Email:FromName"] ?? "VARA";

            // Gmail (i većina pružatelja) šalje samo s adrese računa koji se prijavljuje.
            if (host.Contains("gmail", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(user))
                fromEmail = user;

            using var message = new MailMessage
            {
                From = new MailAddress(fromEmail, fromName),
                Subject = subject,
                IsBodyHtml = true
            };
            message.To.Add(to);

            var view = AlternateView.CreateAlternateViewFromString(
                htmlBody, System.Text.Encoding.UTF8, "text/html");
            foreach (var img in inlineImages)
            {
                var bytes = Convert.FromBase64String(img.Base64Data);
                var res = new LinkedResource(new MemoryStream(bytes), img.MimeType)
                {
                    ContentId = img.ContentId,
                    TransferEncoding = System.Net.Mime.TransferEncoding.Base64
                };
                view.LinkedResources.Add(res);
            }
            message.AlternateViews.Add(view);

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = !bool.TryParse(_config["Smtp__EnableSsl"] ?? _config["Smtp:EnableSsl"], out var ssl) || ssl,
                Credentials = string.IsNullOrWhiteSpace(user) ? null : new NetworkCredential(user, pass),
                Timeout = 20000
            };

            try
            {
                await client.SendMailAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SMTP slanje nije uspjelo ({Host}:{Port})", host, port);
                throw new Exception("SMTP failed: " + ex.Message, ex);
            }

            _logger.LogInformation("Email sent via SMTP to {To}", to);
        }

        private async Task SendViaSendGridAsync(
            string apiKey, string to, string subject, string htmlBody, List<InlineImageAttachment> inlineImages)
        {
            var fromEmail = _config["Email__From"] ?? _config["Email:From"] ?? "adminvaraapp@gmail.com";
            var fromName = _config["Email__FromName"] ?? _config["Email:FromName"] ?? "VARA";

            // Slanje "kao" @gmail.com adresa preko SendGrida ne prolazi DMARC
            // (Gmail/Yahoo ih odbacuju ili tiho brišu, čak i iz spama) — pošiljatelj
            // mora biti na vlastitoj domeni s autentikacijom u SendGridu.
            if (fromEmail.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase)
                || fromEmail.EndsWith("@googlemail.com", StringComparison.OrdinalIgnoreCase))
                _logger.LogWarning(
                    "Email__From je {From} (besplatna adresa) — poruke će vjerojatno biti odbačene zbog DMARC-a. " +
                    "Postavi adresu s vlastite domene i autentificiraj domenu u SendGridu.", fromEmail);

            var client = new SendGridClient(apiKey);
            var from = new EmailAddress(fromEmail, fromName);
            var toAddress = new EmailAddress(to);

            var msg = new SendGridMessage
            {
                From = from,
                Subject = subject,
                HtmlContent = htmlBody
            };
            msg.AddTo(toAddress);

            // Inline slike kao attachmenti s Content-ID
            foreach (var img in inlineImages)
            {
                msg.AddAttachment(new SendGrid.Helpers.Mail.Attachment
                {
                    Content = img.Base64Data,
                    Type = img.MimeType,
                    Filename = img.FileName,
                    Disposition = "inline",
                    ContentId = img.ContentId
                });
            }

            var response = await client.SendEmailAsync(msg);

            if ((int)response.StatusCode >= 400)
            {
                var body = await response.Body.ReadAsStringAsync();
                _logger.LogError("SendGrid error {Status}: {Body}", response.StatusCode, body);
                throw new Exception($"SendGrid failed: {response.StatusCode}");
            }

            _logger.LogInformation("Email sent via SendGrid to {To}", to);
        }
    }
}