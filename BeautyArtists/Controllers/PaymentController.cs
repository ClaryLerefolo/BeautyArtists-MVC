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

        // ─── ✅ FIXED: CORRECT PRICING CONSTANTS ───
        private const decimal CLIENT_MARKUP_RATE = 0.04m;      // 4% card processing fee
        private const decimal BOOKING_FEE = 5.00m;              // Flat R5 booking fee
        private const decimal NEW_CLIENT_COMMISSION = 0.10m;   // 10% for new clients
        private const decimal REPEAT_CLIENT_FLAT_FEE = 15.00m; // R15 for repeat clients
        private const decimal MIN_PLATFORM_FEE = 8.00m;        // Minimum fee floor

        // ─── ✅ FIXED: CORRECT PRICING HELPERS ───
        private decimal CalculateCardProcessingFee(decimal servicePrice)
        {
            return servicePrice * CLIENT_MARKUP_RATE;
        }

        private decimal CalculateClientTotal(decimal servicePrice)
        {
            return servicePrice + CalculateCardProcessingFee(servicePrice) + BOOKING_FEE;
        }

        private decimal CalculateDepositAmount(decimal servicePrice)
        {
            decimal halfService = servicePrice / 2;
            decimal cardFee = CalculateCardProcessingFee(servicePrice);
            return halfService + cardFee + BOOKING_FEE;
        }

        private decimal CalculateFinalAmount(decimal servicePrice)
        {
            return servicePrice / 2;
        }

        private decimal CalculateArtistPayout(decimal artistPrice, bool isNewClient)
        {
            decimal platformFee = isNewClient
                ? artistPrice * NEW_CLIENT_COMMISSION
                : REPEAT_CLIENT_FLAT_FEE;
            platformFee = Math.Max(platformFee, MIN_PLATFORM_FEE);
            return artistPrice - platformFee;
        }

        private async Task<bool> IsNewClient(string customerId, string artistId)
        {
            var existingBookings = await _context.Bookings
                .Where(b => b.CustomerId == customerId
                            && b.UserService.ArtistId == artistId
                            && b.Status != BookingStatus.Cancelled
                            && b.Status != BookingStatus.Rejected)
                .AnyAsync();
            return !existingBookings;
        }

        // ─── HELPER: Build deposit email body ───
        private string BuildDepositEmailBody(Booking booking, decimal depositAmount, decimal finalAmount, bool isFullPayment)
        {
            var serviceName = booking.UserService?.Service?.Name ?? "your service";
            var artistFullName = $"{booking.UserService?.Artist?.FirstName ?? ""} {booking.UserService?.Artist?.LastName ?? ""}".Trim() ?? "The artist";
            var formattedDate = booking.AppointmentDate.ToString("dddd, MMMM dd, yyyy");
            var formattedTime = booking.AppointmentDate.ToString("hh:mm tt");
            var servicePrice = booking.ServicePrice;
            var cardFee = booking.CardProcessingFee;
            var bookingFee = booking.BookingFee;
            var totalAmount = booking.TotalAmount;

            if (isFullPayment)
            {
                return $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #28a745; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
    <h2 style='color: #28a745; text-align: center;'>🎉 Full Payment Received!</h2>
    <p>Dear {booking.Customer?.FirstName ?? "Client"},</p>
    <p>Your full payment of <strong>R{totalAmount:N2}</strong> has been received.</p>
    <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
        <p><strong>📋 Service:</strong> {serviceName}</p>
        <p><strong>👤 Artist:</strong> {artistFullName}</p>
        <p><strong>📅 Date:</strong> {formattedDate}</p>
        <p><strong>⏰ Time:</strong> {formattedTime}</p>
    </div>
    <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
        <p style='margin: 4px 0; display: flex; justify-content: space-between;'>
            <span>Service Price:</span>
            <span>R {servicePrice:N2}</span>
        </p>
        <p style='margin: 4px 0; display: flex; justify-content: space-between;'>
            <span>Card Processing Fee (4%):</span>
            <span>R {cardFee:N2}</span>
        </p>
        <p style='margin: 4px 0; display: flex; justify-content: space-between; border-bottom: 1px solid #333; padding-bottom: 8px;'>
            <span>Booking Fee:</span>
            <span>R {bookingFee:N2}</span>
        </p>
        <p style='margin: 4px 0; display: flex; justify-content: space-between; font-size: 1.1rem;'>
            <strong>Total Paid:</strong>
            <strong style='color: #28a745;'>R {totalAmount:N2}</strong>
        </p>
    </div>
    <p>Your appointment is now <strong style='color: #28a745;'>CONFIRMED</strong> and <strong style='color: #28a745;'>FULLY PAID</strong>.</p>
    <p>Thank you for choosing RubiOr! ✨</p>
    <hr style='border-color: #333;'>
    <p style='font-size: 12px; color: #666;'>RubiOr</p>
</div>";
            }
            else
            {
                return $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #f0c808; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
    <h2 style='color: #f0c808; text-align: center;'>✅ Deposit Received!</h2>
    <p>Dear {booking.Customer?.FirstName ?? "Client"},</p>
    <p>Your deposit of <strong>R{depositAmount:N2}</strong> has been received.</p>
    <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
        <p><strong>📋 Service:</strong> {serviceName}</p>
        <p><strong>👤 Artist:</strong> {artistFullName}</p>
        <p><strong>📅 Date:</strong> {formattedDate}</p>
        <p><strong>⏰ Time:</strong> {formattedTime}</p>
    </div>
    <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
        <p style='margin: 4px 0; display: flex; justify-content: space-between;'>
            <span>Deposit Amount:</span>
            <span>R {depositAmount:N2}</span>
        </p>
        <p style='margin: 4px 0; display: flex; justify-content: space-between; border-bottom: 1px solid #333; padding-bottom: 8px;'>
            <span>Remaining Balance:</span>
            <span>R {finalAmount:N2}</span>
        </p>
        <p style='margin: 4px 0; display: flex; justify-content: space-between; font-size: 1.1rem;'>
            <strong>Total:</strong>
            <strong style='color: #FFD700;'>R {totalAmount:N2}</strong>
        </p>
    </div>
    <p>Your appointment is now <strong style='color: #f0c808;'>CONFIRMED</strong>.</p>
    <p><strong>Remaining Balance:</strong> R {finalAmount:N2} (to be paid at least 2 days before the appointment)</p>
    <p>Thank you for choosing RubiOr! ✨</p>
    <hr style='border-color: #333;'>
    <p style='font-size: 12px; color: #666;'>RubiOr</p>
</div>";
            }
        }

        // ─── HELPER: Build final payment email body ───
        private string BuildFinalPaymentEmailBody(Booking booking, decimal finalAmount)
        {
            var serviceName = booking.UserService?.Service?.Name ?? "your service";
            var artistFullName = $"{booking.UserService?.Artist?.FirstName ?? ""} {booking.UserService?.Artist?.LastName ?? ""}".Trim() ?? "The artist";
            var formattedDate = booking.AppointmentDate.ToString("dddd, MMMM dd, yyyy");
            var formattedTime = booking.AppointmentDate.ToString("hh:mm tt");
            var servicePrice = booking.ServicePrice;
            var cardFee = booking.CardProcessingFee;
            var bookingFee = booking.BookingFee;
            var totalAmount = booking.TotalAmount;

            return $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #28a745; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
    <h2 style='color: #28a745; text-align: center;'>💰 Final Payment Received!</h2>
    <p>Dear {booking.Customer?.FirstName ?? "Client"},</p>
    <p>Your final payment of <strong>R{finalAmount:N2}</strong> has been received.</p>
    <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
        <p><strong>📋 Service:</strong> {serviceName}</p>
        <p><strong>👤 Artist:</strong> {artistFullName}</p>
        <p><strong>📅 Date:</strong> {formattedDate}</p>
        <p><strong>⏰ Time:</strong> {formattedTime}</p>
    </div>
    <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
        <p style='margin: 4px 0; display: flex; justify-content: space-between;'>
            <span>Service Price:</span>
            <span>R {servicePrice:N2}</span>
        </p>
        <p style='margin: 4px 0; display: flex; justify-content: space-between;'>
            <span>Card Processing Fee (4%):</span>
            <span>R {cardFee:N2}</span>
        </p>
        <p style='margin: 4px 0; display: flex; justify-content: space-between; border-bottom: 1px solid #333; padding-bottom: 8px;'>
            <span>Booking Fee:</span>
            <span>R {bookingFee:N2}</span>
        </p>
        <p style='margin: 4px 0; display: flex; justify-content: space-between; font-size: 1.1rem;'>
            <strong>Total Paid:</strong>
            <strong style='color: #28a745;'>R {totalAmount:N2}</strong>
        </p>
    </div>
    <p>Your appointment is now <strong style='color: #28a745;'>FULLY PAID</strong>.</p>
    <p>Thank you for choosing RubiOr! ✨</p>
    <hr style='border-color: #333;'>
    <p style='font-size: 12px; color: #666;'>RubiOr</p>
</div>";
        }

        // ─── HELPER: Build artist notification email ───
        private string BuildArtistPaymentEmail(Booking booking, decimal amount, string paymentType)
        {
            var serviceName = booking.UserService?.Service?.Name ?? "your service";
            var clientFullName = $"{booking.Customer?.FirstName ?? ""} {booking.Customer?.LastName ?? ""}".Trim() ?? "Client";
            var formattedDate = booking.AppointmentDate.ToString("dddd, MMMM dd, yyyy");
            var formattedTime = booking.AppointmentDate.ToString("hh:mm tt");
            var totalAmount = booking.TotalAmount;
            var servicePrice = booking.ServicePrice;

            return $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #f0c808; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
    <h2 style='color: #f0c808; text-align: center;'>💰 {paymentType} Received!</h2>
    <p>Dear {booking.UserService?.Artist?.FirstName ?? "Artist"},</p>
    <p>The client <strong>{clientFullName}</strong> has paid <strong>R{amount:N2}</strong> for:</p>
    <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
        <p><strong>📋 Service:</strong> {serviceName}</p>
        <p><strong>📅 Date:</strong> {formattedDate}</p>
        <p><strong>⏰ Time:</strong> {formattedTime}</p>
        <p><strong>Amount Received:</strong> R{amount:N2}</p>
        <p><strong>Total Booking Amount:</strong> R{totalAmount:N2}</p>
        <p><strong>Your Earnings:</strong> R{servicePrice:N2}</p>
    </div>
    <p>This appointment is now <strong style='color: #f0c808;'>CONFIRMED</strong>.</p>
    <hr style='border-color: #333;'>
    <p style='font-size: 12px; color: #666;'>RubiOr</p>
</div>";
        }

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
                    .Include(b => b.UserService)
                        .ThenInclude(us => us.Artist)
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

                string subaccount = null;
                if (booking.UserService?.Artist != null)
                {
                    var artistProfile = await _context.ArtistProfiles
                        .FirstOrDefaultAsync(p => p.UserId == booking.UserService.ArtistId);

                    if (artistProfile != null && !string.IsNullOrEmpty(artistProfile.SubaccountCode))
                    {
                        if (!artistProfile.SubaccountCode.StartsWith("TEST_SUBACCOUNT_"))
                        {
                            subaccount = artistProfile.SubaccountCode;
                        }
                    }
                }

                var result = await _paymentService.InitializePayment(email, amount, bookingId, subaccount);

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
                    .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
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

                decimal finalAmount = CalculateFinalAmount(booking.ServicePrice);

                if (finalAmount <= 0 || booking.FinalPaymentPaid >= finalAmount)
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

                string subaccount = null;
                if (booking.UserService?.Artist != null)
                {
                    var artistProfile = await _context.ArtistProfiles
                        .FirstOrDefaultAsync(p => p.UserId == booking.UserService.ArtistId);

                    if (artistProfile != null && !string.IsNullOrEmpty(artistProfile.SubaccountCode))
                    {
                        if (!artistProfile.SubaccountCode.StartsWith("TEST_SUBACCOUNT_"))
                            subaccount = artistProfile.SubaccountCode;
                    }
                }

                var result = await _paymentService.InitializePayment(email, finalAmount, bookingId, subaccount);

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
        [Route("Payment/PaymentCallback")]
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

                if (!result.success || result.data?.status != "success")
                {
                    TempData["Error"] = $"Payment verification failed: {result.message ?? "Unknown error"}";
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

                var booking = payment.Booking;
                if (booking == null)
                {
                    TempData["Error"] = "Booking not found.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                payment.Status = "success";
                payment.PaidAt = DateTime.UtcNow;
                payment.PaymentMethod = result.data.channel;
                await _context.SaveChangesAsync();

                bool isDeposit = !booking.IsDepositPaid;
                decimal clientTotal = CalculateClientTotal(booking.ServicePrice);
                bool isFullPayment = payment.Amount >= clientTotal;

                if (isDeposit)
                {
                    if (isFullPayment)
                    {
                        booking.DepositPaid = payment.Amount;
                        booking.DepositPaidDate = DateTime.UtcNow;
                        booking.IsDepositPaid = true;
                        booking.FinalPaymentPaid = 0;
                        booking.Status = BookingStatus.Confirmed;
                        await _context.SaveChangesAsync();

                        string clientEmailBody = BuildDepositEmailBody(booking, payment.Amount, 0, true);
                        string artistEmailBody = BuildArtistPaymentEmail(booking, payment.Amount, "Full Payment");

                        if (!string.IsNullOrEmpty(booking.Customer?.Email))
                            await _emailService.SendEmailAsync(booking.Customer.Email, "✅ Full Payment Confirmed – Appointment Confirmed!", clientEmailBody);

                        if (!string.IsNullOrEmpty(booking.UserService?.Artist?.Email))
                            await _emailService.SendEmailAsync(booking.UserService.Artist.Email, "💰 Full Payment Received – Appointment Confirmed!", artistEmailBody);

                        TempData["Success"] = "Full payment successful! Your appointment is now confirmed and fully paid.";
                    }
                    else
                    {
                        booking.DepositPaid = payment.Amount;
                        booking.DepositPaidDate = DateTime.UtcNow;
                        booking.IsDepositPaid = true;
                        booking.Status = BookingStatus.Confirmed;
                        await _context.SaveChangesAsync();

                        decimal finalAmount = CalculateFinalAmount(booking.ServicePrice);
                        string clientEmailBody = BuildDepositEmailBody(booking, payment.Amount, finalAmount, false);
                        string artistEmailBody = BuildArtistPaymentEmail(booking, payment.Amount, "Deposit");

                        if (!string.IsNullOrEmpty(booking.Customer?.Email))
                            await _emailService.SendEmailAsync(booking.Customer.Email, "✅ Deposit Paid – Appointment Confirmed!", clientEmailBody);

                        if (!string.IsNullOrEmpty(booking.UserService?.Artist?.Email))
                            await _emailService.SendEmailAsync(booking.UserService.Artist.Email, "💰 Deposit Payment Received – Appointment Confirmed!", artistEmailBody);

                        TempData["Success"] = "Deposit successful! Your appointment is now confirmed.";
                    }

                    if (User.Identity.IsAuthenticated)
                    {
                        var currentUser = await _userManager.FindByIdAsync(booking.CustomerId);
                        if (currentUser != null)
                        {
                            string notifTitle = isFullPayment ? "Full Payment Received! 💰" : "Deposit Received! 💰";
                            string notifMsg = isFullPayment
                                ? $"Your full payment of R{payment.Amount:N2} has been received. Appointment CONFIRMED!"
                                : $"Your deposit of R{payment.Amount:N2} has been received. Appointment CONFIRMED!";
                            await _notificationService.CreateNotificationAsync(
                                booking.CustomerId,
                                notifTitle,
                                notifMsg,
                                "payment_received",
                                booking.Id.ToString(),
                                Url.Action("MyBookings", "Booking")
                            );
                        }
                        var artist = await _userManager.FindByIdAsync(booking.UserService.ArtistId);
                        if (artist != null)
                        {
                            string notifTitle = isFullPayment ? "Full Payment Received! 🎉" : "Deposit Paid! 🎉";
                            string notifMsg = isFullPayment
                                ? $"{currentUser?.FirstName} paid the full amount. Appointment confirmed."
                                : $"{currentUser?.FirstName} paid the deposit. Appointment confirmed.";
                            await _notificationService.CreateNotificationAsync(
                                artist.Id,
                                notifTitle,
                                notifMsg,
                                "payment_received",
                                booking.Id.ToString(),
                                Url.Action("MyAppointments", "Artist")
                            );
                        }
                    }
                }
                else
                {
                    decimal finalAmount = CalculateFinalAmount(booking.ServicePrice);
                    booking.FinalPaymentPaid = finalAmount;
                    booking.FinalPaidDate = DateTime.UtcNow;
                    await _context.SaveChangesAsync();

                    string clientEmailBody = BuildFinalPaymentEmailBody(booking, finalAmount);
                    string artistEmailBody = BuildArtistPaymentEmail(booking, finalAmount, "Final Payment");

                    if (!string.IsNullOrEmpty(booking.Customer?.Email))
                        await _emailService.SendEmailAsync(booking.Customer.Email, "✅ Final Payment Confirmed!", clientEmailBody);

                    if (!string.IsNullOrEmpty(booking.UserService?.Artist?.Email))
                        await _emailService.SendEmailAsync(booking.UserService.Artist.Email, "💰 Final Payment Received – Appointment Fully Paid!", artistEmailBody);

                    if (User.Identity.IsAuthenticated)
                    {
                        await _notificationService.CreateNotificationAsync(
                            booking.UserService.ArtistId,
                            "Final Payment Received! 💵",
                            $"{booking.Customer?.FirstName} paid the remaining balance. Appointment fully paid!",
                            "payment_received",
                            booking.Id.ToString(),
                            Url.Action("MyAppointments", "Artist")
                        );
                    }

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

                if (existingPayment?.Booking?.IsDepositPaid == true ||
                    existingPayment?.Booking?.FinalPaymentPaid > 0)
                {
                    TempData["Success"] = "Payment successful! Your booking is updated.";
                }
                else
                {
                    TempData["Error"] = "An error occurred processing your payment. Please contact support.";
                }

                return RedirectToAction("MyBookings", "Booking");
            }
        }
    }
}