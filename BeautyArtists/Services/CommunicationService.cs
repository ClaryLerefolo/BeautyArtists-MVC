using BeautyArtists.Data;
using BeautyArtists.Models;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;

namespace BeautyArtists.Services
{
    public class CommunicationService : ICommunicationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailSender _emailSender;

        public CommunicationService(ApplicationDbContext context, IEmailSender emailSender)
        {
            _context = context;
            _emailSender = emailSender;
        }

        public async Task SendBookingConfirmationToClientAsync(string clientId, int bookingId)
        {
            var client = await _context.Users.FindAsync(clientId);
            if (client == null)
            {
                Console.WriteLine($"❌ SendBookingConfirmation: Client {clientId} not found.");
                return;
            }
            if (string.IsNullOrEmpty(client.Email))
            {
                Console.WriteLine($"❌ SendBookingConfirmation: Client {clientId} has no email.");
                return;
            }

            string subject = "Your Booking is Confirmed - Beauty in Red and Gold";
            string body = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; border: 1px solid #d4af37; padding: 20px;'>
                    <h2 style='color: #8b0000;'>Booking Confirmed!</h2>
                    <p>Hi {client.FirstName},</p>
                    <p>Thank you for your booking. Your request (Reference: #{bookingId}) has been successfully submitted to your chosen artist.</p>
                    <p style='margin-top: 20px;'>Log in to your dashboard to view schedule alterations or directions.</p>
                </div>";

            await _emailSender.SendEmailAsync(client.Email, subject, body);
            Console.WriteLine($"✅ Booking confirmation email sent to client {client.Email}");
        }

        public async Task SendBookingRequestToArtistAsync(string artistId, int bookingId)
        {
            var artist = await _context.Users.FindAsync(artistId);
            if (artist == null)
            {
                Console.WriteLine($"❌ SendBookingRequestToArtist: Artist {artistId} not found.");
                return;
            }
            if (string.IsNullOrEmpty(artist.Email))
            {
                Console.WriteLine($"❌ SendBookingRequestToArtist: Artist {artistId} has no email.");
                return;
            }

            var booking = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.Customer)
                .FirstOrDefaultAsync(b => b.Id == bookingId);

            if (booking == null)
            {
                Console.WriteLine($"❌ SendBookingRequestToArtist: Booking {bookingId} not found.");
                return;
            }

            string subject = "New Booking Appointment Request Received";
            string body = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; border: 1px solid #d4af37; padding: 20px;'>
                    <h2 style='color: #8b0000;'>New Appointment Request</h2>
                    <p>Hello {artist.FirstName},</p>
                    <p>You have received a new appointment request from a client (Reference: #{bookingId}).</p>
                    <p>Please review the timing slot, location parameters (Walk-In or House Call), and accept or decline the request within your profile panel.</p>
                </div>";

            await _emailSender.SendEmailAsync(artist.Email, subject, body);
            Console.WriteLine($"✅ Booking request email sent to artist {artist.Email}");
        }

        public async Task SendBookingStatusUpdateAsync(string recipientId, int bookingId, string status)
        {
            var user = await _context.Users.FindAsync(recipientId);
            if (user == null)
            {
                Console.WriteLine($"❌ SendBookingStatusUpdate: User {recipientId} not found.");
                return;
            }
            if (string.IsNullOrEmpty(user.Email))
            {
                Console.WriteLine($"❌ SendBookingStatusUpdate: User {recipientId} has no email.");
                return;
            }

            string subject = $"Booking Update: Reference #{bookingId}";
            string body = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; border: 1px solid #d4af37; padding: 20px;'>
                    <h2 style='color: #8b0000;'>Appointment Status Changed</h2>
                    <p>Hi {user.FirstName},</p>
                    <p>The status of appointment <strong>#{bookingId}</strong> has been updated to: <span style='color:#8b0000; font-weight:bold;'>{status}</span>.</p>
                </div>";

            await _emailSender.SendEmailAsync(user.Email, subject, body);
            Console.WriteLine($"✅ Status update email sent to {user.Email}");
        }

        public async Task SendDirectMessageEmailAsync(string senderId, string recipientId, string messageSubject, string messageBody)
        {
            var sender = await _context.Users.FindAsync(senderId);
            var recipient = await _context.Users.FindAsync(recipientId);

            if (sender == null)
            {
                Console.WriteLine($"❌ SendDirectMessage: Sender {senderId} not found.");
                return;
            }
            if (recipient == null)
            {
                Console.WriteLine($"❌ SendDirectMessage: Recipient {recipientId} not found.");
                return;
            }
            if (string.IsNullOrEmpty(recipient.Email))
            {
                Console.WriteLine($"❌ SendDirectMessage: Recipient {recipientId} has no email.");
                return;
            }

            bool isHtmlContent = messageBody.Contains("<div") || messageBody.Contains("<html") || messageBody.Contains("<!DOCTYPE");
            string subject = messageSubject;
            string body = isHtmlContent ? messageBody : $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; border: 1px solid #d4af37; padding: 20px;'>
                    <h3 style='color: #8b0000; margin-top:0;'>Message via Beauty in Red and Gold</h3>
                    <p style='font-size: 12px; color: #555;'>From: {sender.FirstName} {sender.LastName}</p>
                    <hr style='border: 0; border-top: 1px solid #eee; margin: 15px 0;' />
                    <p style='white-space: pre-line; line-height: 1.6;'>{messageBody}</p>
                    <hr style='border: 0; border-top: 1px solid #eee; margin: 15px 0;' />
                    <p style='font-size: 11px; color: #888;'>Please use the application portal to continue conversations.</p>
                </div>";

            await _emailSender.SendEmailAsync(recipient.Email, subject, body);
            Console.WriteLine($"✅ Direct message email sent to {recipient.Email}");
        }
    }
}