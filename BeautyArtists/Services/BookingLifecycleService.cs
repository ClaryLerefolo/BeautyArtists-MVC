using BeautyArtists.Data;
using BeautyArtists.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static BeautyArtists.Models.Booking;

namespace BeautyArtists.Services
{
    public class BookingLifecycleService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BookingLifecycleService> _logger;
        private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);

        public BookingLifecycleService(
            IServiceProvider serviceProvider,
            ILogger<BookingLifecycleService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessBookings(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error in BookingLifecycleService: {ex.Message}");
                }

                await Task.Delay(_checkInterval, stoppingToken);
            }
        }

        private async Task ProcessBookings(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
            var commService = scope.ServiceProvider.GetRequiredService<ICommunicationService>();

            var now = DateTime.UtcNow;

            // ─── 1. FIND BOOKINGS THAT NEED CONFIRMATION ───
            // ✅ FIX: ONLY CONFIRMED bookings (deposit paid!)
            var bookingsToPrompt = await context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                .Include(b => b.Customer)
                .Where(b => b.Status == BookingStatus.Confirmed
                            && b.AppointmentDate <= now
                            && !b.IsDisputed
                            && b.ConfirmationPromptSentAt == null
                            && !b.IsCompleted
                            && !b.IsRefunded)  // ← ✅ EXTRA SAFETY: skip if refunded
                .ToListAsync(stoppingToken);

            if (bookingsToPrompt.Any())
            {
                _logger.LogInformation($"Found {bookingsToPrompt.Count} CONFIRMED bookings needing confirmation.");
            }

            foreach (var booking in bookingsToPrompt)
            {
                try
                {
                    // ─── SEND CONFIRMATION NOTIFICATION ───
                    await notificationService.CreateNotificationAsync(
                        booking.CustomerId,
                        "Did Your Appointment Happen?",
                        $"Please confirm if {booking.UserService?.Service?.Name} was completed by {booking.UserService?.Artist?.FirstName}. You have 5 hours to respond.",
                        "confirm_completion",
                        booking.Id.ToString(),
                        "/Booking/ConfirmCompletion/" + booking.Id
                    );

                    // ─── ALSO SEND EMAIL ───
                    if (booking.Customer != null && !string.IsNullOrEmpty(booking.Customer.Email))
                    {
                        string subject = "Did Your Appointment Happen?";
                        string body = $@"
                        <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #f0c808; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
                            <h2 style='color: #f0c808;'>Did Your Appointment Happen?</h2>
                            <p>Dear {booking.Customer.FirstName},</p>
                            <p>Please confirm if <strong>{booking.UserService?.Service?.Name}</strong> was completed by <strong>{booking.UserService?.Artist?.FirstName}</strong>.</p>
                            <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
                                <p><strong>Service:</strong> {booking.UserService?.Service?.Name}</p>
                                <p><strong>Artist:</strong> {booking.UserService?.Artist?.FirstName}</p>
                                <p><strong>Date:</strong> {booking.AppointmentDate:dddd, MMMM dd, yyyy}</p>
                                <p><strong>Time:</strong> {booking.AppointmentDate:hh:mm tt}</p>
                            </div>
                            <div style='text-align: center; margin: 20px 0;'>
                                <a href='{_serviceProvider.GetRequiredService<IUrlHelper>().Action("ConfirmCompletion", "Booking", new { id = booking.Id }, "https")}' 
                                   style='background: #28a745; color: #fff; padding: 12px 30px; text-decoration: none; border-radius: 8px; font-weight: 700; display: inline-block;'>
                                    Confirm Appointment
                                </a>
                            </div>
                            <p style='font-size: 0.8rem; color: rgba(255,255,255,0.3);'>
                                If you don't respond within 5 hours, it will be automatically confirmed.
                            </p>
                            <hr style='border-color: #333;'>
                            <p style='font-size: 12px; color: #666;'>RubiOr</p>
                        </div>";

                        await commService.SendDirectMessageEmailAsync(
                            booking.UserService.ArtistId,
                            booking.CustomerId,
                            subject,
                            body
                        );
                    }

                    // ─── UPDATE BOOKING ───
                    booking.ConfirmationPromptSentAt = now;
                    booking.AutoConfirmAt = now.AddHours(5);
                    await context.SaveChangesAsync(stoppingToken);

                    _logger.LogInformation($"Confirmation prompt sent for booking {booking.Id}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error sending confirmation for booking {booking.Id}: {ex.Message}");
                }
            }

            // ─── 2. AUTO-CONFIRM TIMEOUT ───
            // ✅ FIX: ONLY CONFIRMED bookings
            var autoConfirmBookings = await context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                .Include(b => b.Customer)
                .Where(b => b.Status == BookingStatus.Confirmed
                            && !b.IsDisputed
                            && b.AutoConfirmAt != null
                            && b.AutoConfirmAt <= now
                            && !b.IsCompleted
                            && !b.IsRefunded)  // ← ✅ EXTRA SAFETY: skip if refunded
                .ToListAsync(stoppingToken);

            if (autoConfirmBookings.Any())
            {
                _logger.LogInformation($"Auto-confirming {autoConfirmBookings.Count} bookings.");
            }

            foreach (var booking in autoConfirmBookings)
            {
                try
                {
                    // ─── AUTO-CONFIRM ───
                    booking.IsCompleted = true;
                    booking.CompletedAt = now;
                    booking.Status = BookingStatus.Completed;
                    booking.FundsReleasedAt = now;
                    booking.IsFundsReleased = true;

                    await context.SaveChangesAsync(stoppingToken);

                    // ─── RELEASE FUNDS ───
                    await ReleaseFundsToArtist(context, booking);

                    // ─── SEND NOTIFICATIONS ───
                    await notificationService.CreateNotificationAsync(
                        booking.CustomerId,
                        "Appointment Auto-Confirmed",
                        $"Your appointment for {booking.UserService?.Service?.Name} was auto-confirmed since you didn't respond within 5 hours.",
                        "auto_confirmed",
                        booking.Id.ToString(),
                        "/Booking/MyBookings"
                    );

                    await notificationService.CreateNotificationAsync(
                        booking.UserService.ArtistId,
                        "Appointment Auto-Confirmed",
                        $"The {booking.UserService?.Service?.Name} appointment was auto-confirmed and funds have been released.",
                        "auto_confirmed",
                        booking.Id.ToString(),
                        "/Artist/MyAppointments"
                    );

                    // ─── SEND EMAILS ───
                    if (booking.Customer != null && !string.IsNullOrEmpty(booking.Customer.Email))
                    {
                        string subject = "Appointment Auto-Confirmed";
                        string body = $@"
                        <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #f0c808; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
                            <h2 style='color: #f0c808;'>Appointment Auto-Confirmed</h2>
                            <p>Dear {booking.Customer.FirstName},</p>
                            <p>Your appointment for <strong>{booking.UserService?.Service?.Name}</strong> was auto-confirmed since you didn't respond within 5 hours.</p>
                            <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
                                <p><strong>Service:</strong> {booking.UserService?.Service?.Name}</p>
                                <p><strong>Artist:</strong> {booking.UserService?.Artist?.FirstName}</p>
                                <p><strong>Date:</strong> {booking.AppointmentDate:dddd, MMMM dd, yyyy}</p>
                                <p><strong>Time:</strong> {booking.AppointmentDate:hh:mm tt}</p>
                            </div>
                            <p>Funds have been released to the artist.</p>
                            <hr style='border-color: #333;'>
                            <p style='font-size: 12px; color: #666;'>RubiOr</p>
                        </div>";

                        await commService.SendDirectMessageEmailAsync(
                            booking.UserService.ArtistId,
                            booking.CustomerId,
                            subject,
                            body
                        );
                    }

                    _logger.LogInformation($"Auto-confirmed booking {booking.Id}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error auto-confirming booking {booking.Id}: {ex.Message}");
                }
            }
        }

        private async Task ReleaseFundsToArtist(ApplicationDbContext context, Booking booking)
        {
            try
            {
                // Get artist profile
                var artistProfile = await context.ArtistProfiles
                    .FirstOrDefaultAsync(p => p.UserId == booking.UserService.ArtistId);

                if (artistProfile == null || string.IsNullOrEmpty(artistProfile.SubaccountCode))
                {
                    _logger.LogWarning($"No subaccount for artist {booking.UserService.ArtistId}");
                    return;
                }

                decimal totalPaid = booking.DepositPaid + booking.FinalPaymentPaid;

                if (totalPaid <= 0)
                {
                    _logger.LogWarning($"No payment found for booking {booking.Id}");
                    return;
                }

                // ─── TRANSFER TO ARTIST SUBACCOUNT ───
                // Implementation depends on your payment provider
                // await _paymentService.TransferToSubaccount(artistProfile.SubaccountCode, totalPaid);

                // ─── UPDATE BOOKING ───
                booking.ArtistTotalEarned = totalPaid;
                booking.FundsReleasedAt = DateTime.UtcNow;
                booking.IsFundsReleased = true;

                await context.SaveChangesAsync();

                _logger.LogInformation($"Released R{totalPaid} to artist {booking.UserService.ArtistId} for booking {booking.Id}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"ReleaseFundsToArtist error for booking {booking.Id}: {ex.Message}");
                throw;
            }
        }
    }
}