using Microsoft.AspNetCore.Identity.UI.Services;
using System.Threading.Tasks;

namespace BeautyArtists.Services
{
    public class EmailService : IEmailService
    {
        private readonly IEmailSender _emailSender;

        public EmailService(IEmailSender emailSender)
        {
            _emailSender = emailSender;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            if (string.IsNullOrEmpty(toEmail))
            {
                throw new System.Exception("Recipient email is null or empty.");
            }

            await _emailSender.SendEmailAsync(toEmail, subject, body);
        }
    }
}