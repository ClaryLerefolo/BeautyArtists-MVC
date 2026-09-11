using BeautyArtists.Data;
using BeautyArtists.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using static BeautyArtists.Models.Booking;

namespace BeautyArtists.Services
{
    public class DepositReminderJob
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public DepositReminderJob(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task SendReminders()
        {
            Console.WriteLine($"DepositReminderJob running at {DateTime.UtcNow}");

            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var commService = scope.ServiceProvider.GetRequiredService<ICommunicationService>();

            var now = DateTime.UtcNow;

            var bookings = await context.Bookings
                .Include(b => b.Customer)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                .Where(b => b.Status == BookingStatus.Accepted
                            && !b.IsDepositPaid
                            && b.PaymentWindowOpen
                            && b.DepositDueDate.HasValue
                            && b.DepositDueDate > now)
                .ToListAsync();

            Console.WriteLine($"Found {bookings.Count} active bookings needing reminders");

            foreach (var booking in bookings)
            {
                var timeLeft = booking.DepositDueDate.Value - now;
                bool updated = false;

                Console.WriteLine($"Booking {booking.Id}: {timeLeft.TotalHours:F2}h left");

                if (timeLeft.TotalHours <= 8 && timeLeft.TotalHours > 7 && !booking.PaymentReminder1SentAt.HasValue)
                {
                    booking.PaymentReminder1SentAt = now;
                    await SendReminderEmail(booking, "8 Hours Left to Pay Your Deposit", 8, commService);
                    updated = true;
                }

                if (timeLeft.TotalHours <= 4 && timeLeft.TotalHours > 3 && !booking.PaymentReminder2SentAt.HasValue)
                {
                    booking.PaymentReminder2SentAt = now;
                    await SendReminderEmail(booking, "4 Hours Left to Pay Your Deposit", 4, commService);
                    updated = true;
                }

                if (timeLeft.TotalHours <= 1 && timeLeft.TotalHours > 0 && !booking.PaymentReminder3SentAt.HasValue)
                {
                    booking.PaymentReminder3SentAt = now;
                    await SendReminderEmail(booking, "Final Reminder — 1 Hour Left to Pay Your Deposit", 1, commService);
                    updated = true;
                }

                if (updated)
                {
                    await context.SaveChangesAsync();
                    Console.WriteLine($"Updated booking {booking.Id} – reminder sent");
                }
            }
        }

        private async Task SendReminderEmail(Booking booking, string subject, int hoursRemaining, ICommunicationService commService)
        {
            try
            {
                Console.WriteLine($"Attempting to send to {booking.Customer?.Email}: {subject}");

                var depositUrl = $"https://rubior.co.za/Booking/CheckoutDeposit/{booking.Id}";
                var serviceName = booking.UserService?.Service?.Name ?? "your service";
                var depositAmount = booking.DepositAmount;
                var customerName = booking.Customer?.FirstName ?? "Client";
                var deadline = booking.DepositDueDate.Value.ToString("dddd, dd MMMM yyyy 'at' HH:mm");

                bool isFinal = hoursRemaining <= 1;

                string body = $@"
                <div style='font-family: Arial, sans-serif; max-width: 560px; margin: 0 auto; padding: 32px 24px; background: #0a0a0a; color: #eaeaea; border-radius: 10px;'>

                    <p style='font-size: 11px; letter-spacing: 3px; text-transform: uppercase; color: #C9A84C; margin: 0 0 8px;'>
                        RubiOr
                    </p>

                    <h1 style='font-size: 20px; font-weight: 600; color: #C9A84C; margin: 0 0 24px;'>
                        Deposit Reminder
                    </h1>

                    <p style='font-size: 14px; line-height: 1.6; margin: 0 0 14px; color: #eaeaea;'>
                        Hi {customerName},
                    </p>

                    <p style='font-size: 14px; line-height: 1.6; margin: 0 0 24px; color: #c4c4c4;'>
                        Your deposit for <strong style='color: #eaeaea;'>{serviceName}</strong> is still outstanding.
                        You have <strong style='color: #C9A84C;'>{hoursRemaining} hour{(hoursRemaining > 1 ? "s" : "")}</strong> left to complete payment.
                    </p>

                    <table style='width: 100%; font-size: 14px; border-collapse: collapse; margin: 0 0 24px;'>
                        <tr>
                            <td style='padding: 10px 0; color: #8a8a8a; border-bottom: 1px solid #1e1e1e;'>Deposit amount</td>
                            <td style='padding: 10px 0; text-align: right; color: #C9A84C; font-weight: 600; border-bottom: 1px solid #1e1e1e;'>R {depositAmount:N2}</td>
                        </tr>
                        <tr>
                            <td style='padding: 10px 0; color: #8a8a8a;'>Deadline</td>
                            <td style='padding: 10px 0; text-align: right; color: #eaeaea; font-weight: 600;'>{deadline}</td>
                        </tr>
                    </table>

                    <p style='text-align: center; margin: 0 0 24px;'>
                        <a href='{depositUrl}' style='display: inline-block; padding: 12px 32px; background: #C9A84C; color: #0a0a0a; text-decoration: none; border-radius: 6px; font-weight: 600; font-size: 14px;'>
                            Pay Deposit
                        </a>
                    </p>

                    {(isFinal ? @"
                    <p style='font-size: 13px; line-height: 1.6; color: #d9534f; margin: 0 0 20px; border-left: 2px solid #8B0000; padding: 8px 12px; background: rgba(139,0,0,0.08);'>
                        Please note: if payment is not received by the deadline, the booking will be cancelled automatically and the slot released.
                    </p>" : "")}

                    <hr style='border: none; border-top: 1px solid #1e1e1e; margin: 24px 0 16px;' />

                    <p style='font-size: 11px; color: #6a6a6a; line-height: 1.6; margin: 0; text-align: center;'>
                        If you've already paid, please disregard this email.
                    </p>

                </div>";

                await commService.SendDirectMessageEmailAsync(
                    booking.UserService?.ArtistId ?? "system",
                    booking.CustomerId,
                    subject,
                    body);

                Console.WriteLine($"Email sent to {booking.Customer?.Email}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Reminder email error for booking {booking.Id}: {ex.Message}");
                Console.WriteLine($"Stack: {ex.StackTrace}");
            }
        }
    }
}