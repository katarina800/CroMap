using System.Net;
using System.Net.Mail;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace CroMap.Services
{
    public class EmailService : IEmailServiceWithInlineImages
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

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
            // Dva puta: prvo SendGrid; ako ga račun odbije (npr. "Maximum credits
            // exceeded" kad se potroši besplatna kvota) ili nema ključa, pokušava se
            // SMTP (Smtp__Host/User/Password) — npr. Gmail s lozinkom aplikacije ili
            // Brevo. Bez ijednog postavljenog pružatelja slanje vraća grešku.
            var apiKey = _config["SendGrid__ApiKey"] ?? _config["SendGrid:ApiKey"] ?? "";
            var smtpHost = _config["Smtp__Host"] ?? _config["Smtp:Host"] ?? "";
            var smtpConfigured = !string.IsNullOrWhiteSpace(smtpHost);

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                if (!smtpConfigured)
                    throw new InvalidOperationException(
                        "Nije postavljen ni SendGrid__ApiKey ni Smtp__Host — e-mail se ne može poslati.");
                await SendViaSmtpAsync(to, subject, htmlBody, inlineImages);
                return;
            }

            try
            {
                await SendViaSendGridAsync(apiKey, to, subject, htmlBody, inlineImages);
            }
            catch (Exception ex) when (smtpConfigured)
            {
                _logger.LogWarning(ex, "SendGrid nije poslao poruku — pokušavam SMTP.");
                await SendViaSmtpAsync(to, subject, htmlBody, inlineImages);
            }
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
                msg.AddAttachment(new Attachment
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