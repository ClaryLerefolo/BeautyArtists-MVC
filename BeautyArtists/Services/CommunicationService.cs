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
            try
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

                var booking = await _context.Bookings
                    .Include(b => b.UserService)
                        .ThenInclude(us => us.Service)
                    .Include(b => b.UserService)
                        .ThenInclude(us => us.Artist)
                    .FirstOrDefaultAsync(b => b.Id == bookingId);

                string artistName = booking?.UserService?.Artist != null
                    ? $"{booking.UserService.Artist.FirstName} {booking.UserService.Artist.LastName}".Trim()
                    : "your artist";

                string serviceName = booking?.UserService?.Service?.Name ?? "Service";
                string appointmentDate = booking?.AppointmentDate.ToString("dddd, MMMM dd, yyyy") ?? "TBD";
                string appointmentTime = booking?.AppointmentDate.ToString("hh:mm tt") ?? "TBD";

                string subject = "📤 Booking Request Sent!";
                string body = $@"
        <div style='font-family: Arial, sans-serif; max-width: 600px; border: 2px solid #28a745; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
            <h2 style='color: #28a745; text-align: center;'>📤 Booking Request Sent!</h2>
            <p>Dear {client.FirstName},</p>
            <p>Your booking request has been sent to <strong>{artistName}</strong>.</p>
            <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
                <p><strong>Service:</strong> {serviceName}</p>
                <p><strong>Date:</strong> {appointmentDate}</p>
                <p><strong>Time:</strong> {appointmentTime}</p>
                <p><strong>Artist:</strong> {artistName}</p>
            </div>
            <p>You will receive a notification when the artist responds.</p>
            <hr>
            <p style='font-size: 12px; color: #666;'>Beauty Artists Hub</p>
        </div>";

                await _emailSender.SendEmailAsync(client.Email, subject, body);
                Console.WriteLine($"✅ Booking confirmation email sent to client {client.Email}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ SendBookingConfirmationToClientAsync error: {ex.Message}");
            }
        }

        // 🔥 FIXED: Send Booking Request to Artist
        public async Task SendBookingRequestToArtistAsync(string artistId, int bookingId)
        {
            try
            {
                Console.WriteLine($"🔍 SendBookingRequestToArtist: ArtistId={artistId}, BookingId={bookingId}");

                // 1. Get artist
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
                Console.WriteLine($"✅ Artist found: {artist.Email}");

                // 2. Get booking with all details
                var booking = await _context.Bookings
                    .Include(b => b.Customer)
                    .Include(b => b.UserService)
                        .ThenInclude(us => us.Service)
                    .Include(b => b.UserService)
                        .ThenInclude(us => us.Artist)
                    .FirstOrDefaultAsync(b => b.Id == bookingId);

                if (booking == null)
                {
                    Console.WriteLine($"❌ SendBookingRequestToArtist: Booking {bookingId} not found.");
                    return;
                }
                Console.WriteLine($"✅ Booking found: {booking.Id}");

                // 3. Build email with FULL DETAILS
                string clientName = booking.Customer != null
                    ? $"{booking.Customer.FirstName} {booking.Customer.LastName}".Trim()
                    : "Client";
                string serviceName = booking.UserService?.Service?.Name ?? "Service";
                string artistName = !string.IsNullOrEmpty(artist.FirstName) ? artist.FirstName : "Artist";
                string appointmentDate = booking.AppointmentDate.ToString("dddd, MMMM dd, yyyy");
                string appointmentTime = booking.AppointmentDate.ToString("hh:mm tt");
                string locationType = booking.SelectedLocationType == LocationType.HouseCall ? "🏠 House Call" : "🏢 Walk-In";
                string price = $"R {booking.ServicePrice:N2}";
                string dashboardUrl = "https://beautyinredandgold-fka9avbqb8esb0at.southafricanorth-01.azurewebsites.net/Artist/MyAppointments";

                string subject = "📅 New Booking Request!";
                string body = $@"
        <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #f0c808; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
            <div style='text-align: center; margin-bottom: 20px;'>
                <h1 style='color: #f0c808; margin: 0; font-size: 24px;'>📅 New Booking Request!</h1>
                <hr style='border-color: #f0c808;'>
            </div>
            
            <p style='font-size: 16px;'>Dear <strong>{artistName}</strong>,</p>
            
            <p style='font-size: 14px; color: #ddd;'>You have a new booking request from <strong style='color: #f0c808;'>{clientName}</strong>.</p>
            
            <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
                <h3 style='color: #f0c808; margin-top: 0;'>📋 Booking Details</h3>
                <p style='margin: 8px 0;'><strong>Client:</strong> {clientName}</p>
                <p style='margin: 8px 0;'><strong>Service:</strong> {serviceName}</p>
                <p style='margin: 8px 0;'><strong>Date:</strong> {appointmentDate}</p>
                <p style='margin: 8px 0;'><strong>Time:</strong> {appointmentTime}</p>
                <p style='margin: 8px 0;'><strong>Location:</strong> {locationType}</p>
                <p style='margin: 8px 0;'><strong>Service Price:</strong> {price}</p>
            </div>
            
            <div style='text-align: center; margin: 25px 0;'>
                <a href='{dashboardUrl}' 
                   style='background: linear-gradient(45deg, #f0c808, #e50914); 
                          color: #000; 
                          padding: 14px 30px; 
                          text-decoration: none; 
                          border-radius: 50px; 
                          font-weight: bold; 
                          font-size: 16px; 
                          display: inline-block;'>
                    👀 View in Dashboard
                </a>
            </div>
            
            <div style='background: rgba(229, 9, 20, 0.1); padding: 12px; border-radius: 8px; margin: 15px 0; border-left: 4px solid #e50914;'>
                <p style='margin: 0; font-size: 12px; color: #ff8888;'>
                    <strong>⚠️ Action Required:</strong> Please log in to review and accept or decline this booking request.
                </p>
            </div>
            
            <hr style='border-color: #333; margin: 20px 0;'>
            <p style='font-size: 11px; color: #666; text-align: center;'>
                &copy; {DateTime.Now.Year} Beauty Artists Hub
            </p>
        </div>";

                // 4. Send the email
                await _emailSender.SendEmailAsync(artist.Email, subject, body);
                Console.WriteLine($"✅ Booking request email sent to artist {artist.Email}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ SendBookingRequestToArtistAsync error: {ex.Message}");
                Console.WriteLine($"❌ Stack: {ex.StackTrace}");
            }
        }

        public async Task SendBookingStatusUpdateAsync(string recipientId, int bookingId, string status)
        {
            try
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
        <div style='font-family: Arial, sans-serif; max-width: 600px; border: 2px solid #f0c808; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
            <h2 style='color: #f0c808; text-align: center;'>Appointment Status Changed</h2>
            <p>Hi {user.FirstName},</p>
            <p>The status of appointment <strong>#{bookingId}</strong> has been updated to: <span style='color:#28a745; font-weight:bold;'>{status}</span>.</p>
            <hr>
            <p style='font-size: 12px; color: #666;'>Beauty Artists Hub</p>
        </div>";

                await _emailSender.SendEmailAsync(user.Email, subject, body);
                Console.WriteLine($"✅ Status update email sent to {user.Email}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ SendBookingStatusUpdateAsync error: {ex.Message}");
            }
        }

        public async Task SendDirectMessageEmailAsync(string senderId, string recipientId, string messageSubject, string messageBody)
        {
            try
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
        <div style='font-family: Arial, sans-serif; max-width: 600px; border: 2px solid #f0c808; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
            <h3 style='color: #f0c808; margin-top:0;'>Message via Beauty in Red and Gold</h3>
            <p style='font-size: 12px; color: #888;'>From: {sender.FirstName} {sender.LastName}</p>
            <hr style='border: 0; border-top: 1px solid #333; margin: 15px 0;' />
            <p style='white-space: pre-line; line-height: 1.6;'>{messageBody}</p>
            <hr style='border: 0; border-top: 1px solid #333; margin: 15px 0;' />
            <p style='font-size: 11px; color: #666;'>Please use the application portal to continue conversations.</p>
        </div>";

                await _emailSender.SendEmailAsync(recipient.Email, subject, body);
                Console.WriteLine($"✅ Direct message email sent to {recipient.Email}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ SendDirectMessageEmailAsync error: {ex.Message}");
            }
        }
    }
}