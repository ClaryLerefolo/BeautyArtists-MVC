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

        // ─── PRICING CONSTANTS ───
        private const decimal CLIENT_MARKUP_RATE = 0.04m;
        private const decimal BOOKING_FEE = 5.00m;
        private const decimal NEW_CLIENT_COMMISSION = 0.10m;
        private const decimal REPEAT_CLIENT_FLAT_FEE = 15.00m;
        private const decimal MIN_PLATFORM_FEE = 8.00m;

        // ─── HELPERS ───
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

        // ─── ✅ FIXED: IsNewClient based on SPECIFIC SERVICE (UserServiceId) ───
        private async Task<bool> IsNewClient(string customerId, int userServiceId)
        {
            var existingBookings = await _context.Bookings
                .Where(b => b.CustomerId == customerId
                            && b.UserServiceId == userServiceId
                            && b.Status != BookingStatus.Cancelled
                            && b.Status != BookingStatus.Rejected)
                .AnyAsync();
            return !existingBookings;
        }

        // ─── BUILD DEPOSIT EMAIL (CLIENT-FRIENDLY - NO PLATFORM FEES) ───
        private string BuildDepositEmailBody(Booking booking, decimal depositAmount, decimal finalAmount, bool isFullPayment)
        {
            // Reload booking with all navigation properties
            var fullBooking = _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                .Include(b => b.Customer)
                .FirstOrDefault(b => b.Id == booking.Id) ?? booking;

            var serviceName = fullBooking.UserService?.Service?.Name ?? "your service";
            var artistFullName = $"{fullBooking.UserService?.Artist?.FirstName ?? ""} {fullBooking.UserService?.Artist?.LastName ?? ""}".Trim() ?? "The artist";
            var formattedDate = fullBooking.AppointmentDate.ToString("dddd, MMMM dd, yyyy");
            var formattedTime = fullBooking.AppointmentDate.ToString("hh:mm tt");
            var servicePrice = fullBooking.ServicePrice;
            var cardFee = fullBooking.CardProcessingFee;
            var bookingFee = fullBooking.BookingFee;
            var totalAmount = fullBooking.TotalAmount;

            if (isFullPayment)
            {
                return $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #28a745; border-radius: 12px; padding: 24px; background: #0a0a0a; color: #fff;'>
    <h2 style='color: #28a745; margin-top: 0;'>🎉 Full Payment Received!</h2>
    <p>Dear {fullBooking.Customer?.FirstName ?? "Client"},</p>
    <p>Your full payment of <strong style='color: #28a745;'>R{totalAmount:N2}</strong> has been received.</p>
    
    <div style='background: #1a1a1a; padding: 16px; border-radius: 10px; margin: 16px 0; border-left: 4px solid #28a745;'>
        <p style='margin: 6px 0;'><strong style='color: #28a745;'>📋 Service:</strong> <span style='color: #fff;'>{serviceName}</span></p>
        <p style='margin: 6px 0;'><strong style='color: #28a745;'>👤 Artist:</strong> <span style='color: #fff;'>{artistFullName}</span></p>
        <p style='margin: 6px 0;'><strong style='color: #28a745;'>📅 Date:</strong> <span style='color: #fff;'>{formattedDate}</span></p>
        <p style='margin: 6px 0;'><strong style='color: #28a745;'>⏰ Time:</strong> <span style='color: #fff;'>{formattedTime}</span></p>
    </div>
    
    <div style='background: #1a1a1a; padding: 16px; border-radius: 10px; margin: 16px 0;'>
        <p style='margin: 6px 0; display: flex; justify-content: space-between;'>
            <span style='color: rgba(255,255,255,0.6);'>Service Price:</span>
            <span style='color: #fff; font-weight: 600;'>R{servicePrice:N2}</span>
        </p>
        <p style='margin: 6px 0; display: flex; justify-content: space-between;'>
            <span style='color: rgba(255,255,255,0.6);'>Card Processing Fee (4%):</span>
            <span style='color: #fff; font-weight: 600;'>R{cardFee:N2}</span>
        </p>
        <p style='margin: 6px 0; display: flex; justify-content: space-between; border-bottom: 1px solid #2a2a2a; padding-bottom: 10px;'>
            <span style='color: rgba(255,255,255,0.6);'>Booking Fee:</span>
            <span style='color: #fff; font-weight: 600;'>R{bookingFee:N2}</span>
        </p>
        <p style='margin: 10px 0 0 0; display: flex; justify-content: space-between; font-size: 18px; border-top: 2px solid #28a745; padding-top: 12px;'>
            <strong style='color: #28a745;'>Total Paid:</strong>
            <strong style='color: #28a745;'>R{totalAmount:N2}</strong>
        </p>
    </div>
    
    <p>Your appointment is now <strong style='color: #28a745;'>CONFIRMED</strong> and <strong style='color: #28a745;'>FULLY PAID</strong>.</p>
    <p>Thank you for choosing RubiOr! ✨</p>
    <hr style='border-color: #2a2a2a;'>
    <p style='font-size: 12px; color: rgba(255,255,255,0.2);'>This is an automated message. Please do not reply.</p>
</div>";
            }
            else
            {
                return $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #f0c808; border-radius: 12px; padding: 24px; background: #0a0a0a; color: #fff;'>
    <h2 style='color: #f0c808; margin-top: 0;'>✅ Deposit Received!</h2>
    <p>Dear {fullBooking.Customer?.FirstName ?? "Client"},</p>
    <p>Your deposit of <strong style='color: #FFD700;'>R{depositAmount:N2}</strong> has been received.</p>
    
    <div style='background: #1a1a1a; padding: 16px; border-radius: 10px; margin: 16px 0; border-left: 4px solid #f0c808;'>
        <p style='margin: 6px 0;'><strong style='color: #f0c808;'>📋 Service:</strong> <span style='color: #fff;'>{serviceName}</span></p>
        <p style='margin: 6px 0;'><strong style='color: #f0c808;'>👤 Artist:</strong> <span style='color: #fff;'>{artistFullName}</span></p>
        <p style='margin: 6px 0;'><strong style='color: #f0c808;'>📅 Date:</strong> <span style='color: #fff;'>{formattedDate}</span></p>
        <p style='margin: 6px 0;'><strong style='color: #f0c808;'>⏰ Time:</strong> <span style='color: #fff;'>{formattedTime}</span></p>
    </div>
    
    <div style='background: #1a1a1a; padding: 16px; border-radius: 10px; margin: 16px 0;'>
        <p style='margin: 6px 0; display: flex; justify-content: space-between;'>
            <span style='color: rgba(255,255,255,0.6);'>Service Price:</span>
            <span style='color: #fff; font-weight: 600;'>R{servicePrice:N2}</span>
        </p>
        <p style='margin: 6px 0; display: flex; justify-content: space-between;'>
            <span style='color: rgba(255,255,255,0.6);'>Card Processing Fee (4%):</span>
            <span style='color: #fff; font-weight: 600;'>R{cardFee:N2}</span>
        </p>
        <p style='margin: 6px 0; display: flex; justify-content: space-between; border-bottom: 1px solid #2a2a2a; padding-bottom: 10px;'>
            <span style='color: rgba(255,255,255,0.6);'>Booking Fee:</span>
            <span style='color: #fff; font-weight: 600;'>R{bookingFee:N2}</span>
        </p>
        <p style='margin: 10px 0 0 0; display: flex; justify-content: space-between; font-size: 18px; border-top: 2px solid #f0c808; padding-top: 12px;'>
            <strong style='color: #f0c808;'>Total:</strong>
            <strong style='color: #FFD700;'>R{totalAmount:N2}</strong>
        </p>
    </div>
    
    <div style='background: #1a1a1a; padding: 16px; border-radius: 10px; margin: 16px 0; border-left: 4px solid #FFD700;'>
        <p style='margin: 6px 0; display: flex; justify-content: space-between;'>
            <span style='color: rgba(255,255,255,0.7);'>💰 Deposit Paid:</span>
            <span style='color: #FFD700; font-weight: 700;'>R{depositAmount:N2}</span>
        </p>
        <p style='margin: 6px 0 0 0; display: flex; justify-content: space-between; border-top: 1px solid #2a2a2a; padding-top: 8px;'>
            <span style='color: rgba(255,255,255,0.4);'>Remaining balance:</span>
            <span style='color: #fff; font-weight: 600;'>R{finalAmount:N2}</span>
        </p>
    </div>
    
    <p>Your appointment is now <strong style='color: #f0c808;'>CONFIRMED</strong>.</p>
    <p style='color: rgba(255,255,255,0.6); font-size: 14px;'>💡 <strong>Remaining Balance:</strong> R{finalAmount:N2} (to be paid at least 2 days before the appointment)</p>
    <p>Thank you for choosing RubiOr! ✨</p>
    <hr style='border-color: #2a2a2a;'>
    <p style='font-size: 12px; color: rgba(255,255,255,0.2);'>This is an automated message. Please do not reply.</p>
</div>";
            }
        }

        // ─── BUILD FINAL PAYMENT EMAIL (CLIENT-FRIENDLY - NO PLATFORM FEES) ───
        private string BuildFinalPaymentEmailBody(Booking booking, decimal finalAmount)
        {
            var fullBooking = _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                .Include(b => b.Customer)
                .FirstOrDefault(b => b.Id == booking.Id) ?? booking;

            var serviceName = fullBooking.UserService?.Service?.Name ?? "your service";
            var artistFullName = $"{fullBooking.UserService?.Artist?.FirstName ?? ""} {fullBooking.UserService?.Artist?.LastName ?? ""}".Trim() ?? "The artist";
            var formattedDate = fullBooking.AppointmentDate.ToString("dddd, MMMM dd, yyyy");
            var formattedTime = fullBooking.AppointmentDate.ToString("hh:mm tt");
            var servicePrice = fullBooking.ServicePrice;
            var cardFee = fullBooking.CardProcessingFee;
            var bookingFee = fullBooking.BookingFee;
            var totalAmount = fullBooking.TotalAmount;

            return $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #28a745; border-radius: 12px; padding: 24px; background: #0a0a0a; color: #fff;'>
    <h2 style='color: #28a745; margin-top: 0;'>💰 Final Payment Received!</h2>
    <p>Dear {fullBooking.Customer?.FirstName ?? "Client"},</p>
    <p>Your final payment of <strong style='color: #28a745;'>R{finalAmount:N2}</strong> has been received.</p>
    
    <div style='background: #1a1a1a; padding: 16px; border-radius: 10px; margin: 16px 0; border-left: 4px solid #28a745;'>
        <p style='margin: 6px 0;'><strong style='color: #28a745;'>📋 Service:</strong> <span style='color: #fff;'>{serviceName}</span></p>
        <p style='margin: 6px 0;'><strong style='color: #28a745;'>👤 Artist:</strong> <span style='color: #fff;'>{artistFullName}</span></p>
        <p style='margin: 6px 0;'><strong style='color: #28a745;'>📅 Date:</strong> <span style='color: #fff;'>{formattedDate}</span></p>
        <p style='margin: 6px 0;'><strong style='color: #28a745;'>⏰ Time:</strong> <span style='color: #fff;'>{formattedTime}</span></p>
    </div>
    
    <div style='background: #1a1a1a; padding: 16px; border-radius: 10px; margin: 16px 0;'>
        <p style='margin: 6px 0; display: flex; justify-content: space-between;'>
            <span style='color: rgba(255,255,255,0.6);'>Service Price:</span>
            <span style='color: #fff; font-weight: 600;'>R{servicePrice:N2}</span>
        </p>
        <p style='margin: 6px 0; display: flex; justify-content: space-between;'>
            <span style='color: rgba(255,255,255,0.6);'>Card Processing Fee (4%):</span>
            <span style='color: #fff; font-weight: 600;'>R{cardFee:N2}</span>
        </p>
        <p style='margin: 6px 0; display: flex; justify-content: space-between; border-bottom: 1px solid #2a2a2a; padding-bottom: 10px;'>
            <span style='color: rgba(255,255,255,0.6);'>Booking Fee:</span>
            <span style='color: #fff; font-weight: 600;'>R{bookingFee:N2}</span>
        </p>
        <p style='margin: 10px 0 0 0; display: flex; justify-content: space-between; font-size: 18px; border-top: 2px solid #28a745; padding-top: 12px;'>
            <strong style='color: #28a745;'>Total Paid:</strong>
            <strong style='color: #28a745;'>R{totalAmount:N2}</strong>
        </p>
    </div>
    
    <p>Your appointment is now <strong style='color: #28a745;'>FULLY PAID</strong>.</p>
    <p>Thank you for choosing RubiOr! ✨</p>
    <hr style='border-color: #2a2a2a;'>
    <p style='font-size: 12px; color: rgba(255,255,255,0.2);'>This is an automated message. Please do not reply.</p>
</div>";
        }

        // ─── BUILD ARTIST NOTIFICATION EMAIL ───
        private string BuildArtistPaymentEmail(Booking booking, decimal amount, string paymentType)
        {
            var fullBooking = _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                .Include(b => b.Customer)
                .FirstOrDefault(b => b.Id == booking.Id) ?? booking;

            var serviceName = fullBooking.UserService?.Service?.Name ?? "your service";
            var clientFullName = $"{fullBooking.Customer?.FirstName ?? ""} {fullBooking.Customer?.LastName ?? ""}".Trim() ?? "Client";
            var formattedDate = fullBooking.AppointmentDate.ToString("dddd, MMMM dd, yyyy");
            var formattedTime = fullBooking.AppointmentDate.ToString("hh:mm tt");
            var totalAmount = fullBooking.TotalAmount;
            var servicePrice = fullBooking.ServicePrice;

            return $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #f0c808; border-radius: 12px; padding: 24px; background: #0a0a0a; color: #fff;'>
    <h2 style='color: #f0c808; margin-top: 0;'>💰 {paymentType} Received!</h2>
    <p>Dear {fullBooking.UserService?.Artist?.FirstName ?? "Artist"},</p>
    <p>The client <strong style='color: #FFD700;'>{clientFullName}</strong> has paid <strong style='color: #FFD700;'>R{amount:N2}</strong> for:</p>
    
    <div style='background: #1a1a1a; padding: 16px; border-radius: 10px; margin: 16px 0; border-left: 4px solid #f0c808;'>
        <p style='margin: 6px 0;'><strong style='color: #f0c808;'>📋 Service:</strong> <span style='color: #fff;'>{serviceName}</span></p>
        <p style='margin: 6px 0;'><strong style='color: #f0c808;'>📅 Date:</strong> <span style='color: #fff;'>{formattedDate}</span></p>
        <p style='margin: 6px 0;'><strong style='color: #f0c808;'>⏰ Time:</strong> <span style='color: #fff;'>{formattedTime}</span></p>
    </div>
    
    <div style='background: #1a1a1a; padding: 16px; border-radius: 10px; margin: 16px 0;'>
        <p style='margin: 6px 0; display: flex; justify-content: space-between;'>
            <span style='color: rgba(255,255,255,0.6);'>Amount Received:</span>
            <span style='color: #FFD700; font-weight: 600;'>R{amount:N2}</span>
        </p>
        <p style='margin: 6px 0; display: flex; justify-content: space-between; border-bottom: 1px solid #2a2a2a; padding-bottom: 8px;'>
            <span style='color: rgba(255,255,255,0.6);'>Total Booking Amount:</span>
            <span style='color: #fff; font-weight: 600;'>R{totalAmount:N2}</span>
        </p>
        <p style='margin: 8px 0 0 0; display: flex; justify-content: space-between; font-size: 16px; border-top: 2px solid #f0c808; padding-top: 10px;'>
            <strong style='color: #f0c808;'>Your Earnings:</strong>
            <strong style='color: #00c853;'>R{servicePrice:N2}</strong>
        </p>
    </div>
    
    <p>This appointment is now <strong style='color: #f0c808;'>CONFIRMED</strong>.</p>
    <hr style='border-color: #2a2a2a;'>
    <p style='font-size: 12px; color: rgba(255,255,255,0.2);'>RubiOr</p>
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