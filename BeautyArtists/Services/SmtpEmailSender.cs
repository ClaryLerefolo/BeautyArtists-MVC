using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace BeautyArtists.Services
{
    public class SmtpEmailSender : IEmailSender
    {
        private readonly IConfiguration _config;

        public SmtpEmailSender(IConfiguration config)
        {
            _config = config;
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            try
            {
                var smtpSettings = _config.GetSection("SmtpSettings");

                Console.WriteLine($"📧 [SmtpEmailSender] Sending to: {email}");
                Console.WriteLine($"📧 [SmtpEmailSender] Host: {smtpSettings["Host"]}:{smtpSettings["Port"]}");
                Console.WriteLine($"📧 [SmtpEmailSender] Username: {smtpSettings["Username"]}");
                Console.WriteLine($"📧 [SmtpEmailSender] From: {smtpSettings["FromAddress"]}");

                using (var message = new MailMessage())
                {
                    message.To.Add(new MailAddress(email));
                    message.From = new MailAddress(smtpSettings["FromAddress"], "RubiOr");
                    message.Subject = subject;
                    message.Body = htmlMessage;
                    message.IsBodyHtml = true;

                    using (var client = new SmtpClient(smtpSettings["Host"], int.Parse(smtpSettings["Port"])))
                    {
                        client.Credentials = new NetworkCredential(smtpSettings["Username"], smtpSettings["Password"]);
                        client.EnableSsl = true;
                        client.Timeout = 30000; // 30 seconds

                        Console.WriteLine($"📧 [SmtpEmailSender] Sending...");
                        await client.SendMailAsync(message);
                        Console.WriteLine($"✅ [SmtpEmailSender] Email sent successfully to {email}");
                    }
                }
            }
            catch (SmtpException smtpEx)
            {
                Console.WriteLine($"❌ [SmtpEmailSender] SMTP Error: {smtpEx.Message}");
                Console.WriteLine($"📚 StatusCode: {smtpEx.StatusCode}");
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [SmtpEmailSender] Error: {ex.Message}");
                Console.WriteLine($"📚 Stack: {ex.StackTrace}");
                throw;
            }
        }
    }

    public class SmtpSettings
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string FromAddress { get; set; }
    }
}