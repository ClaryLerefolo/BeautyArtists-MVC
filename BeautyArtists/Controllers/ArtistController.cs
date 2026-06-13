using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Models.ViewModels;
using BeautyArtists.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using static BeautyArtists.Models.Booking;

namespace BeautyArtists.Controllers
{
    [Authorize(Roles = "Artist")]
    public class ArtistController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _env;
        private readonly ICommunicationService _commService;
        private readonly INotificationService _notificationService;  // ← ADD THIS

        // UPDATE CONSTRUCTOR
        public ArtistController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IWebHostEnvironment env, ICommunicationService communicationService, INotificationService notificationService)
        {
            _context = context;
            _userManager = userManager;
            _env = env;
            _commService = communicationService;
            _notificationService = notificationService;  // ← ADD THIS
        }

        // ... (keep all your existing methods: Dashboard, Profile, EditProfile, etc.)

        // This shows ONLY the bookings for the logged-in Artist
        public async Task<IActionResult> MyAppointments()
        {
            var artistId = _userManager.GetUserId(User);

            var myBookings = await _context.Bookings
                .Include(b => b.Customer)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService.Artist)
                .Where(b => b.UserService.ArtistId == artistId)
                .OrderByDescending(b => b.AppointmentDate)
                .ToListAsync();

            return View(myBookings);
        }

        // UPDATE TRANSPORT COST ONLY
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateTransportCost(int bookingId, decimal transportCost)
        {
            var artistId = _userManager.GetUserId(User);

            var booking = await _context.Bookings
                .Include(b => b.UserService)
                .Include(b => b.Customer)
                .FirstOrDefaultAsync(b => b.Id == bookingId && b.UserService.ArtistId == artistId);

            if (booking == null)
                return NotFound();

            if (booking.SelectedLocationType != LocationType.HouseCall)
            {
                TempData["Error"] = "Transport costs only apply to House Call bookings.";
                return RedirectToAction("MyAppointments");
            }

            if (booking.Status != BookingStatus.Pending && booking.Status != BookingStatus.Confirmed)
            {
                TempData["Error"] = "Transport cost can only be set for pending or confirmed bookings.";
                return RedirectToAction("MyAppointments");
            }

            // Update transport cost
            booking.TransportCost = transportCost;
            booking.TotalAmount = (booking.UserService?.Price ?? 0) + transportCost;

            await _context.SaveChangesAsync();

            // Notify client about transport cost addition
            if (booking.Customer != null && !string.IsNullOrEmpty(booking.Customer.Email))
            {
                string subject = "🚗 Transport Cost Added to Your Booking";
                string body = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px;'>
                    <h2 style='color: #f0c808;'>Transport Cost Update</h2>
                    <p>Dear {booking.Customer.FirstName},</p>
                    <p>The artist has added <strong>R{transportCost:N2}</strong> for transport to your location.</p>
                    <p><strong>New Total Amount:</strong> R{booking.TotalAmount:N2}</p>
                    <p>The artist will review and confirm your booking shortly.</p>
                </div>";

                await _commService.SendDirectMessageEmailAsync(artistId, booking.CustomerId, subject, body);
            }

            TempData["Success"] = $"Transport cost of R{transportCost:N2} added successfully!";
            return RedirectToAction("MyAppointments");
        }

        // MAIN ARTIST UPDATE STATUS WITH EMAILS - FIXED WITH NOTIFICATIONS
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ArtistUpdateStatus(int bookingId, BookingStatus newStatus, string artistNotes, decimal transportCost = 0)
        {
            var artistId = _userManager.GetUserId(User);

            var booking = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService.Artist)
                .Include(b => b.Customer)
                .Include(b => b.AvailabilitySlot)
                .FirstOrDefaultAsync(b => b.Id == bookingId && b.UserService.ArtistId == artistId);

            if (booking == null)
                return Unauthorized();

            // Save the artist's notes
            booking.ArtistNotes = artistNotes;

            // Get client info for email
            var client = booking.Customer;
            var clientEmail = client?.Email;
            var clientName = client?.FirstName ?? "Valued Client";

            if (newStatus == BookingStatus.Accepted)  // Artist accepts the booking
            {
                // Handle transport cost for house calls
                if (booking.SelectedLocationType == LocationType.HouseCall && transportCost > 0)
                {
                    booking.TransportCost = transportCost;
                    booking.TotalAmount = (booking.UserService?.Price ?? 0) + transportCost;
                }

                booking.Status = BookingStatus.Accepted;

                if (booking.AvailabilitySlot != null)
                {
                    booking.AvailabilitySlot.IsBooked = true;
                }

                await _context.SaveChangesAsync();

                // ========== SEND IN-APP NOTIFICATION TO CLIENT ==========
                try
                {
                    await _notificationService.CreateNotificationAsync(
                        booking.CustomerId,
                        "Appointment Accepted! ✅",
                        $"Great news! {booking.UserService?.Artist?.FirstName} has ACCEPTED your appointment for {booking.UserService?.Service?.Name} on {booking.AppointmentDate:MMM dd}. Pay your 50% deposit now!",
                        "booking_accepted",
                        booking.Id.ToString(),
                        Url.Action("CheckoutDeposit", "Booking", new { id = booking.Id })
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"In-app notification error (non-critical): {ex.Message}");
                }

                // ========== SEND EMAIL TO CLIENT ==========
                if (!string.IsNullOrEmpty(clientEmail))
                {
                    var depositUrl = Url.Action("CheckoutDeposit", "Booking", new { id = booking.Id }, Request.Scheme);

                    string subject = "✅ Your Appointment Has Been Accepted!";
                    string emailBody = $@"
            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #f0c808; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
                <div style='text-align: center; margin-bottom: 20px;'>
                    <h1 style='color: #f0c808; margin: 0;'>✨ Appointment Accepted! ✨</h1>
                    <hr style='border-color: #f0c808;'>
                </div>
                
                <p>Dear <strong>{clientName}</strong>,</p>
                
                <p>Great news! The artist has <strong style='color: #28a745;'>ACCEPTED</strong> your appointment request.</p>
                
                <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
                    <h3 style='color: #f0c808; margin-top: 0;'>📋 Booking Details</h3>
                    <p><strong>Service:</strong> {booking.UserService?.Service?.Name}</p>
                    <p><strong>Artist:</strong> {booking.UserService?.Artist?.FirstName} {booking.UserService?.Artist?.LastName}</p>
                    <p><strong>Date:</strong> {booking.AppointmentDate:dddd, MMMM dd, yyyy}</p>
                    <p><strong>Time:</strong> {booking.AppointmentDate:hh:mm tt}</p>
                    <p><strong>Location Type:</strong> {(booking.SelectedLocationType == LocationType.HouseCall ? "🏠 House Call" : "🏢 Walk-In")}</p>
                    {(booking.SelectedLocationType == LocationType.HouseCall && !string.IsNullOrEmpty(booking.HouseCallAddress) ? $"<p><strong>📍 Address:</strong> {booking.HouseCallAddress}</p>" : "")}
                </div>
                
                <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
                    <h3 style='color: #f0c808; margin-top: 0;'>💰 Payment Details</h3>
                    <p><strong>Base Price:</strong> R {(booking.UserService?.Price ?? 0):N2}</p>
                    {(booking.TransportCost > 0 ? $"<p><strong>Transport Cost:</strong> R {booking.TransportCost:N2}</p>" : "")}
                    <p><strong>Total Amount:</strong> <span style='color: #f0c808; font-size: 18px;'>R {booking.TotalAmount:N2}</span></p>
                    <hr style='border-color: #333;'>
                    <p><strong>Deposit Required (50%):</strong> <span style='color: #ff6600;'>R {(booking.TotalAmount / 2):N2}</span></p>
                </div>
                
                {(booking.ArtistNotes != null ? $@"
                <div style='background: rgba(240, 200, 8, 0.1); padding: 15px; border-radius: 8px; margin: 15px 0; border-left: 4px solid #f0c808;'>
                    <p><strong>📝 Message from your artist:</strong></p>
                    <p style='color: #ddd; font-style: italic;'>“{booking.ArtistNotes}”</p>
                </div>" : "")}
                
                <div style='text-align: center; margin: 25px 0;'>
                    <a href='{depositUrl}' style='background: linear-gradient(45deg, #f0c808, #e50914); color: #000; padding: 14px 30px; text-decoration: none; border-radius: 50px; font-weight: bold; display: inline-block;'>
                        💰 PAY YOUR 50% DEPOSIT NOW
                    </a>
                </div>
                
                <div style='background: rgba(229, 9, 20, 0.1); padding: 12px; border-radius: 8px; margin: 15px 0; border-left: 4px solid #e50914;'>
                    <p style='margin: 0; font-size: 12px; color: #ff8888;'>
                        <strong>⚠️ IMPORTANT:</strong> Your appointment is not confirmed until the 50% deposit is paid.
                    </p>
                </div>
                
                <hr>
                <p style='font-size: 11px; color: #666; text-align: center;'>
                    &copy; {DateTime.Now.Year} Beauty Artists Hub
                </p>
            </div>";

                    await _commService.SendDirectMessageEmailAsync(artistId, booking.CustomerId, subject, emailBody);
                }

                TempData["Success"] = "Appointment accepted! Client has been notified to pay deposit.";
            }
            else if (newStatus == BookingStatus.Rejected)
            {
                booking.Status = BookingStatus.Rejected;

                // Free up the slot
                if (booking.AvailabilitySlot != null)
                {
                    booking.AvailabilitySlot.IsBooked = false;
                }

                await _context.SaveChangesAsync();

                // ========== SEND IN-APP NOTIFICATION TO CLIENT ==========
                try
                {
                    await _notificationService.CreateNotificationAsync(
                        booking.CustomerId,
                        "Appointment Declined ❌",
                        $"Unfortunately, your appointment request for {booking.UserService?.Service?.Name} on {booking.AppointmentDate:MMM dd} has been declined.",
                        "booking_rejected",
                        booking.Id.ToString(),
                        Url.Action("MyBookings", "Booking")
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"In-app notification error (non-critical): {ex.Message}");
                }

                // ========== SEND REJECTION EMAIL TO CLIENT ==========
                if (!string.IsNullOrEmpty(clientEmail))
                {
                    string rejectSubject = "❌ Appointment Request Update";
                    string rejectBody = $@"
            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #e50914; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
                <h2 style='color: #e50914; text-align: center;'>Appointment Not Accepted</h2>
                <p>Dear {clientName},</p>
                <p>Unfortunately, your appointment request for <strong>{booking.UserService?.Service?.Name}</strong> on <strong>{booking.AppointmentDate:MMM dd, yyyy} at {booking.AppointmentDate:hh:mm tt}</strong> has been declined.</p>
                {(artistNotes != null ? $"<p><strong>Reason:</strong> {artistNotes}</p>" : "<p>No specific reason was provided.</p>")}
                <p>Please try booking a different time slot or contact the artist directly.</p>
                <hr>
                <p style='font-size: 12px; color: #666;'>Beauty Artists Hub</p>
            </div>";

                    await _commService.SendDirectMessageEmailAsync(artistId, booking.CustomerId, rejectSubject, rejectBody);
                }

                TempData["Success"] = "Appointment request rejected. Client has been notified.";
            }
            else if (newStatus == BookingStatus.Completed)
            {
                booking.Status = BookingStatus.Completed;
                await _context.SaveChangesAsync();

                // ========== SEND IN-APP NOTIFICATION TO CLIENT ==========
                try
                {
                    await _notificationService.CreateNotificationAsync(
                        booking.CustomerId,
                        "Service Completed! ⭐",
                        $"Your {booking.UserService?.Service?.Name} appointment has been completed. Thank you for choosing us!",
                        "booking_completed",
                        booking.Id.ToString(),
                        Url.Action("MyBookings", "Booking")
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"In-app notification error (non-critical): {ex.Message}");
                }

                // ========== SEND COMPLETION EMAIL TO CLIENT ==========
                if (!string.IsNullOrEmpty(clientEmail))
                {
                    string completeSubject = "🎉 Service Completed! Thank You!";
                    string completeBody = $@"
            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #28a745; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
                <h2 style='color: #28a745; text-align: center;'>Service Completed! 🎉</h2>
                <p>Dear {clientName},</p>
                <p>Your <strong>{booking.UserService?.Service?.Name}</strong> appointment has been marked as completed.</p>
                <p>We hope you had a great experience! Thank you for choosing Beauty Artists Hub!</p>
                <p style='text-align: center; margin-top: 20px;'>✨ We hope to see you again soon! ✨</p>
            </div>";

                    await _commService.SendDirectMessageEmailAsync(artistId, booking.CustomerId, completeSubject, completeBody);
                }

                TempData["Success"] = "Service marked as completed! Client has been notified.";
            }

            return RedirectToAction(nameof(MyAppointments));
        }
    }
}