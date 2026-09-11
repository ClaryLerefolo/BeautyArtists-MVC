using BeautyArtists.Data;
using BeautyArtists.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using static BeautyArtists.Models.Booking;

namespace BeautyArtists.Services
{
    /// <summary>
    /// ⚠️ TESTING VERSION – uses SECONDS (deadline = 60 seconds).
    /// Revert to hours before production.
    /// </summary>
    public class DepositExpiryJob
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public DepositExpiryJob(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task CancelExpiredBookings()
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
            var commService = scope.ServiceProvider.GetRequiredService<ICommunicationService>();

            var now = DateTime.UtcNow;

            // ─── FETCH EXPIRED BOOKINGS ───
            var expiredBookings = await context.Bookings
                .Include(b => b.Customer)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                .Include(b => b.AvailabilitySlot)
                .Where(b => b.PaymentWindowOpen
                            && b.DepositDueDate != null
                            && b.DepositDueDate <= now
                            && !b.IsDepositPaid
                            && b.Status == BookingStatus.Accepted)
                .ToListAsync();

            if (!expiredBookings.Any())
                return;

            Console.WriteLine($"⏰ Found {expiredBookings.Count} EXPIRED bookings (deposit not paid within deadline)");

            foreach (var booking in expiredBookings)
            {
                try
                {
                    // ─── UPDATE STATUS ───
                    booking.Status = BookingStatus.Cancelled;
                    booking.PaymentWindowOpen = false;
                    booking.ArtistNotes = "Cancelled - Deposit not paid within deadline";

                    // ─── RELEASE SLOT ───
                    if (booking.AvailabilitySlot != null)
                    {
                        booking.AvailabilitySlot.IsBooked = false;
                    }

                    await context.SaveChangesAsync();

                    // ─── SEND CANCELLATION EMAIL TO CLIENT ───
                    if (booking.Customer != null && !string.IsNullOrEmpty(booking.Customer.Email))
                    {
                        string subject = "❌ Booking Cancelled - Deposit Not Paid";
                        string body = BuildCancellationEmail(booking);
                        await commService.SendDirectMessageEmailAsync(
                            booking.UserService?.ArtistId ?? "system",
                            booking.CustomerId,
                            subject,
                            body
                        );
                        Console.WriteLine($"📧 Cancellation email sent to client {booking.Customer.Email} for booking {booking.Id}");
                    }

                    // ─── SEND NOTIFICATION TO ARTIST ───
                    if (booking.UserService?.Artist != null && !string.IsNullOrEmpty(booking.UserService.Artist.Email))
                    {
                        string subject = "❌ Booking Cancelled - Client Didn't Pay Deposit";
                        string body = $@"
                        <div style='font-family: Arial, sans-serif; max-width: 600px; padding: 20px; background: #0a0a0a; color: #fff; border: 2px solid #e50914; border-radius: 12px;'>
                            <h2 style='color: #e50914;'>Booking Cancelled</h2>
                            <p>Dear {booking.UserService.Artist.FirstName},</p>
                            <p>The client <strong>{booking.Customer?.FirstName} {booking.Customer?.LastName}</strong> did not pay the deposit within the deadline.</p>
                            <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
                                <p><strong>Service:</strong> {booking.UserService?.Service?.Name}</p>
                                <p><strong>Date:</strong> {booking.AppointmentDate:dddd, MMMM dd, yyyy}</p>
                                <p><strong>Time:</strong> {booking.AppointmentDate:hh:mm tt}</p>
                            </div>
                            <p>The slot has been released and is now available for other clients.</p>
                            <hr style='border-color: #333;'>
                            <p style='font-size: 12px; color: #666;'>RubiOr</p>
                        </div>";

                        await commService.SendDirectMessageEmailAsync(
                            booking.CustomerId,
                            booking.UserService.ArtistId,
                            subject,
                            body
                        );
                        Console.WriteLine($"📧 Cancellation notification sent to artist {booking.UserService.Artist.Email} for booking {booking.Id}");
                    }

                    // ─── IN-APP NOTIFICATION TO CLIENT ───
                    await notificationService.CreateNotificationAsync(
                        booking.CustomerId,
                        "Booking Cancelled ❌",
                        "Your booking was cancelled because the deposit wasn't paid within the deadline. The slot has been released.",
                        "deposit_expired",
                        booking.Id.ToString(),
                        "/Booking/MyBookings"
                    );

                    // ─── IN-APP NOTIFICATION TO ARTIST ───
                    await notificationService.CreateNotificationAsync(
                        booking.UserService?.ArtistId ?? "system",
                        "Booking Cancelled ❌",
                        $"Booking #{booking.Id} was cancelled - client didn't pay deposit within deadline. Slot released.",
                        "deposit_expired",
                        booking.Id.ToString(),
                        "/Artist/MyAppointments"
                    );

                    Console.WriteLine($"✅ Booking {booking.Id} cancelled due to deposit not paid");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error processing expired booking {booking.Id}: {ex.Message}");
                }
            }
        }

        private string BuildCancellationEmail(Booking booking)
        {
            var serviceName = booking.UserService?.Service?.Name ?? "your service";
            var customerName = booking.Customer?.FirstName ?? "Client";

            return $@"
            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #e50914; border-radius: 12px; padding: 24px; background: #0a0a0a; color: #fff;'>
                <h2 style='color: #e50914; margin-top: 0;'>❌ Booking Cancelled</h2>
                <p>Dear <strong>{customerName}</strong>,</p>
                <p>Your booking for <strong>{serviceName}</strong> has been <strong style='color: #e50914;'>CANCELLED</strong>.</p>
                
                <div style='background: #1a1a1a; padding: 16px; border-radius: 10px; margin: 16px 0; border-left: 4px solid #e50914;'>
                    <p style='margin: 6px 0;'><strong style='color: #e50914;'>Reason:</strong> <span style='color: #fff;'>Deposit was not paid within the deadline</span></p>
                    <p style='margin: 6px 0;'><strong style='color: #e50914;'>Service:</strong> <span style='color: #fff;'>{serviceName}</span></p>
                    <p style='margin: 6px 0;'><strong style='color: #e50914;'>Date:</strong> <span style='color: #fff;'>{booking.AppointmentDate:dddd, MMMM dd, yyyy}</span></p>
                    <p style='margin: 6px 0;'><strong style='color: #e50914;'>Time:</strong> <span style='color: #fff;'>{booking.AppointmentDate:hh:mm tt}</span></p>
                </div>
                
                <p>The slot has been released. If you still wish to book, please <a href='https://rubior.co.za' style='color: #f0c808;'>submit a new booking request</a>.</p>
                
                <hr style='border-color: #2a2a2a;'>
                <p style='font-size: 12px; color: rgba(255,255,255,0.2); text-align: center;'>We hope to see you soon!</p>
            </div>";
        }
    }
}