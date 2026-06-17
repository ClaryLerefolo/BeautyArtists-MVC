using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using static BeautyArtists.Models.Booking;

namespace BeautyArtists.Controllers
{
    public class PaymentController : Controller
    {
        private readonly IPaymentService _paymentService;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ICommunicationService _commService;
        private readonly INotificationService _notificationService;
        private readonly IEmailService _emailService;

        public PaymentController(
            IPaymentService paymentService,
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            ICommunicationService commService,
            INotificationService notificationService,
            IEmailService emailService)
        {
            _paymentService = paymentService;
            _context = context;
            _userManager = userManager;
            _commService = commService;
            _notificationService = notificationService;
            _emailService = emailService;
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InitiatePayment(int bookingId, string email, decimal amount)
        {
            try
            {
                var booking = await _context.Bookings
                    .FirstOrDefaultAsync(b => b.Id == bookingId && b.CustomerId == _userManager.GetUserId(User));

                if (booking == null)
                {
                    TempData["Error"] = "Booking not found.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                if (booking.IsDepositPaid || booking.Status == BookingStatus.Confirmed)
                {
                    TempData["Error"] = "This booking is already confirmed or paid.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                var result = await _paymentService.InitializePayment(email, amount, bookingId);

                if (!result.success)
                {
                    TempData["Error"] = $"Payment initialization failed: {result.message}";
                    return RedirectToAction("CheckoutDeposit", "Booking", new { id = bookingId });
                }

                if (string.IsNullOrEmpty(result.authorizationUrl))
                {
                    TempData["Error"] = "Payment gateway returned an invalid response. Please try again.";
                    return RedirectToAction("CheckoutDeposit", "Booking", new { id = bookingId });
                }

                return Redirect(result.authorizationUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"InitiatePayment Exception: {ex.Message}");
                TempData["Error"] = $"An error occurred: {ex.Message}";
                return RedirectToAction("CheckoutDeposit", "Booking", new { id = bookingId });
            }
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InitiateFinalPayment(int bookingId, string email, decimal amount)
        {
            try
            {
                var booking = await _context.Bookings
                    .FirstOrDefaultAsync(b => b.Id == bookingId && b.CustomerId == _userManager.GetUserId(User));

                if (booking == null)
                {
                    TempData["Error"] = "Booking not found.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                if (booking.Status != BookingStatus.Confirmed)
                {
                    TempData["Error"] = "Booking must be confirmed before final payment.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                if (booking.TotalAmount == 0)
                {
                    TempData["Error"] = "This booking has already been fully paid.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                decimal remainingBalance = booking.TotalAmount / 2;
                if (remainingBalance <= 0)
                {
                    TempData["Error"] = "No remaining balance to pay.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                double daysUntilAppointment = (booking.AppointmentDate.Date - DateTime.Now.Date).TotalDays;
                if (daysUntilAppointment < 2)
                {
                    TempData["Error"] = "Final payment must be cleared at least 2 days before the appointment.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                var result = await _paymentService.InitializePayment(email, remainingBalance, bookingId);

                if (!result.success)
                {
                    TempData["Error"] = $"Payment initialization failed: {result.message}";
                    return RedirectToAction("CheckoutFinalPayment", "Booking", new { id = bookingId });
                }

                if (string.IsNullOrEmpty(result.authorizationUrl))
                {
                    TempData["Error"] = "Payment gateway returned an invalid response. Please try again.";
                    return RedirectToAction("CheckoutFinalPayment", "Booking", new { id = bookingId });
                }

                return Redirect(result.authorizationUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"InitiateFinalPayment Exception: {ex.Message}");
                TempData["Error"] = $"An error occurred: {ex.Message}";
                return RedirectToAction("CheckoutFinalPayment", "Booking", new { id = bookingId });
            }
        }

        [HttpGet]
        public async Task<IActionResult> PaymentCallback(string reference, string trxref)
        {
            string refToVerify = reference ?? trxref;
            if (string.IsNullOrEmpty(refToVerify))
            {
                TempData["Error"] = "Invalid payment reference.";
                return RedirectToAction("MyBookings", "Booking");
            }

            try
            {
                var result = await _paymentService.VerifyPayment(refToVerify);

                if (!result.success)
                {
                    TempData["Error"] = $"Payment verification failed: {result.message}";
                    return RedirectToAction("MyBookings", "Booking");
                }

                if (result.data == null)
                {
                    TempData["Error"] = "Payment verification returned no data.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                if (result.data.status != "success")
                {
                    TempData["Error"] = $"Payment was not successful. Status: {result.data.status}";
                    return RedirectToAction("MyBookings", "Booking");
                }

                var payment = await _context.Payments
                    .Include(p => p.Booking)
                        .ThenInclude(b => b.UserService)
                            .ThenInclude(us => us.Artist)
                    .Include(p => p.Booking.UserService.Service)
                    .Include(p => p.Booking.Customer)
                    .FirstOrDefaultAsync(p => p.Reference == refToVerify);

                if (payment == null)
                {
                    TempData["Error"] = "Payment record not found.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                // ============================================================
                // ✅ HANDLE DUPLICATE CALLBACK (payment already success)
                // ============================================================
                if (payment.Status == "success")
                {
                    var existingBooking = payment.Booking;
                    if (existingBooking != null)
                    {
                        // If deposit wasn't paid, confirm it
                        if (!existingBooking.IsDepositPaid)
                        {
                            existingBooking.IsDepositPaid = true;
                            existingBooking.Status = BookingStatus.Confirmed;
                            await _context.SaveChangesAsync();

                            // Send deposit emails (both artist & client)
                            await SendDepositEmails(existingBooking, payment.Amount);

                            TempData["Success"] = "Payment successful! Your appointment is now confirmed.";
                            return RedirectToAction("MyBookings", "Booking");
                        }

                        // If this is a final payment and TotalAmount > 0, mark as fully paid
                        if (existingBooking.TotalAmount > 0)
                        {
                            decimal remainingBalance = existingBooking.TotalAmount / 2;
                            existingBooking.TotalAmount = 0;
                            await _context.SaveChangesAsync();

                            // Send final payment emails (both artist & client)
                            await SendFinalPaymentEmails(existingBooking, remainingBalance);

                            TempData["Success"] = "Final payment successful! Your appointment is now fully paid.";
                            return RedirectToAction("MyBookings", "Booking");
                        }
                    }

                    TempData["Success"] = "Payment already verified.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                // ============================================================
                // ✅ FRESH PAYMENT
                // ============================================================
                payment.Status = "success";
                payment.PaidAt = DateTime.UtcNow;
                payment.PaymentMethod = result.data.channel;

                var booking = payment.Booking;
                bool isDeposit = !booking.IsDepositPaid;

                if (isDeposit)
                {
                    booking.IsDepositPaid = true;
                    booking.Status = BookingStatus.Confirmed;
                    await _context.SaveChangesAsync();

                    // Send deposit emails
                    await SendDepositEmails(booking, payment.Amount);

                    // In-app notifications
                    var currentUser = await _userManager.FindByIdAsync(booking.CustomerId);
                    var artist = await _userManager.FindByIdAsync(booking.UserService.ArtistId);

                    if (currentUser != null)
                    {
                        await _notificationService.CreateNotificationAsync(
                            booking.CustomerId,
                            "Payment Received! 💰",
                            $"Your deposit of R{payment.Amount:N2} has been received. Appointment CONFIRMED!",
                            "payment_received",
                            booking.Id.ToString(),
                            Url.Action("MyBookings", "Booking")
                        );
                    }
                    if (artist != null)
                    {
                        await _notificationService.CreateNotificationAsync(
                            artist.Id,
                            "Deposit Paid! 🎉",
                            $"{currentUser?.FirstName} paid the deposit. Appointment confirmed.",
                            "payment_received",
                            booking.Id.ToString(),
                            Url.Action("MyAppointments", "Artist")
                        );
                    }

                    TempData["Success"] = "Deposit successful! Your appointment is now confirmed.";
                }
                else
                {
                    // Final payment
                    decimal remainingBalance = booking.TotalAmount / 2;
                    booking.TotalAmount = 0;
                    await _context.SaveChangesAsync();

                    // Send final payment emails
                    await SendFinalPaymentEmails(booking, remainingBalance);

                    // In-app notification to artist
                    await _notificationService.CreateNotificationAsync(
                        booking.UserService.ArtistId,
                        "Final Payment Received! 💵",
                        $"{booking.Customer?.FirstName} paid the remaining balance. Appointment fully paid!",
                        "payment_received",
                        booking.Id.ToString(),
                        Url.Action("MyAppointments", "Artist")
                    );

                    TempData["Success"] = "Final payment successful! Your appointment is now fully paid.";
                }

                return RedirectToAction("MyBookings", "Booking");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PaymentCallback error: {ex.Message}");
                Console.WriteLine($"Stack: {ex.StackTrace}");

                var existingPayment = await _context.Payments
                    .Include(p => p.Booking)
                    .FirstOrDefaultAsync(p => p.Reference == refToVerify);

                if (existingPayment?.Booking?.IsDepositPaid == true)
                {
                    TempData["Success"] = "Payment successful! Your appointment is confirmed.";
                }
                else
                {
                    TempData["Error"] = "An error occurred processing your payment. Please contact support.";
                }

                return RedirectToAction("MyBookings", "Booking");
            }
        }

        // ============================================================
        // 📧 UNIFIED EMAIL HELPERS (BOTH ARTIST & CLIENT)
        // ============================================================

        private async Task SendDepositEmails(Booking booking, decimal depositAmount)
        {
            try
            {
                var artist = booking.UserService?.Artist;
                var client = booking.Customer;
                var serviceName = booking.UserService?.Service?.Name ?? "your service";

                if (artist == null || client == null)
                {
                    Console.WriteLine($"❌ Deposit emails: Artist or client is null. ArtistId: {artist?.Id}, ClientId: {client?.Id}");
                    return;
                }

                string artistEmail = artist.Email;
                string clientEmail = client.Email;

                if (string.IsNullOrEmpty(artistEmail) || string.IsNullOrEmpty(clientEmail))
                {
                    Console.WriteLine($"❌ Deposit emails: Missing email. ArtistEmail={artistEmail}, ClientEmail={clientEmail}");
                    return;
                }

                // 📧 To Artist
                string artistSubject = "💰 Deposit Payment Received – Appointment Confirmed!";
                string artistBody = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #f0c808; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
                    <h2 style='color: #f0c808; text-align: center;'>Deposit Received! ✅</h2>
                    <p>Dear {artist.FirstName},</p>
                    <p>The client <strong>{client.FirstName} {client.LastName}</strong> has paid the 50% deposit of <strong>R{depositAmount:N2}</strong> for:</p>
                    <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
                        <p><strong>Service:</strong> {serviceName}</p>
                        <p><strong>Date:</strong> {booking.AppointmentDate:dddd, MMMM dd, yyyy}</p>
                        <p><strong>Time:</strong> {booking.AppointmentDate:hh:mm tt}</p>
                        <p><strong>Deposit Received:</strong> R{depositAmount:N2}</p>
                        <p><strong>Remaining Balance:</strong> R{(booking.TotalAmount / 2):N2}</p>
                    </div>
                    <p>This appointment is now <strong>CONFIRMED</strong>. The client will pay the remaining balance at least 2 days before the appointment.</p>
                    <hr>
                    <p style='font-size: 12px; color: #666;'>Beauty Artists Hub</p>
                </div>";

                // 📧 To Client
                string clientSubject = "✅ Deposit Paid – Appointment Confirmed!";
                string clientBody = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #28a745; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
                    <h2 style='color: #28a745; text-align: center;'>Deposit Paid! 🎉</h2>
                    <p>Dear {client.FirstName},</p>
                    <p>Your deposit of <strong>R{depositAmount:N2}</strong> has been received.</p>
                    <p>Your appointment for <strong>{serviceName}</strong> on <strong>{booking.AppointmentDate:dddd, MMMM dd, yyyy} at {booking.AppointmentDate:hh:mm tt}</strong> is now <strong>CONFIRMED</strong>.</p>
                    <p><strong>Remaining Balance:</strong> R{(booking.TotalAmount / 2):N2} (to be paid at least 2 days before the appointment)</p>
                    <p>Thank you for choosing Beauty Artists Hub!</p>
                    <hr>
                    <p style='font-size: 12px; color: #666;'>Beauty Artists Hub</p>
                </div>";

                await _emailService.SendEmailAsync(artistEmail, artistSubject, artistBody);
                await _emailService.SendEmailAsync(clientEmail, clientSubject, clientBody);

                Console.WriteLine($"✅ Deposit emails sent to artist: {artistEmail} and client: {clientEmail}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ SendDepositEmails error: {ex.Message}");
            }
        }

        private async Task SendFinalPaymentEmails(Booking booking, decimal remainingBalance)
        {
            try
            {
                var artist = booking.UserService?.Artist;
                var client = booking.Customer;
                var serviceName = booking.UserService?.Service?.Name ?? "your service";

                if (artist == null || client == null)
                {
                    Console.WriteLine($"❌ Final emails: Artist or client is null. ArtistId: {artist?.Id}, ClientId: {client?.Id}");
                    return;
                }

                string artistEmail = artist.Email;
                string clientEmail = client.Email;

                if (string.IsNullOrEmpty(artistEmail) || string.IsNullOrEmpty(clientEmail))
                {
                    Console.WriteLine($"❌ Final emails: Missing email. ArtistEmail={artistEmail}, ClientEmail={clientEmail}");
                    return;
                }

                // 📧 To Artist
                string artistSubject = "💰 Final Payment Received – Appointment Fully Paid!";
                string artistBody = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #28a745; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
                    <h2 style='color: #28a745; text-align: center;'>Final Payment Received! ✅</h2>
                    <p>Dear {artist.FirstName},</p>
                    <p>The client <strong>{client.FirstName} {client.LastName}</strong> has paid the remaining balance of <strong>R{remainingBalance:N2}</strong> for:</p>
                    <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
                        <p><strong>Service:</strong> {serviceName}</p>
                        <p><strong>Date:</strong> {booking.AppointmentDate:dddd, MMMM dd, yyyy}</p>
                        <p><strong>Time:</strong> {booking.AppointmentDate:hh:mm tt}</p>
                        <p><strong>Total Paid:</strong> <span style='color: #28a745;'>R {(booking.UserService?.Price ?? 0):N2}</span></p>
                    </div>
                    <p>This appointment is now <strong>FULLY PAID</strong>. You can mark it as completed after the service.</p>
                    <hr>
                    <p style='font-size: 12px; color: #666;'>Beauty Artists Hub</p>
                </div>";

                // 📧 To Client
                string clientSubject = "✅ Final Payment Confirmed!";
                string clientBody = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #28a745; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
                    <h2 style='color: #28a745; text-align: center;'>Final Payment Confirmed! 🎉</h2>
                    <p>Dear {client.FirstName},</p>
                    <p>Your final payment of <strong>R{remainingBalance:N2}</strong> has been received.</p>
                    <p>Your appointment for <strong>{serviceName}</strong> on <strong>{booking.AppointmentDate:dddd, MMMM dd, yyyy} at {booking.AppointmentDate:hh:mm tt}</strong> is now <strong>FULLY PAID</strong>.</p>
                    <p>Thank you for choosing Beauty Artists Hub!</p>
                    <hr>
                    <p style='font-size: 12px; color: #666;'>Beauty Artists Hub</p>
                </div>";

                await _emailService.SendEmailAsync(artistEmail, artistSubject, artistBody);
                await _emailService.SendEmailAsync(clientEmail, clientSubject, clientBody);

                Console.WriteLine($"✅ Final emails sent to artist: {artistEmail} and client: {clientEmail}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ SendFinalPaymentEmails error: {ex.Message}");
            }
        }
    }
}