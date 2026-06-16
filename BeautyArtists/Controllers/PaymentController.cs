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

        public PaymentController(
            IPaymentService paymentService,
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            ICommunicationService commService,
            INotificationService notificationService)
        {
            _paymentService = paymentService;
            _context = context;
            _userManager = userManager;
            _commService = commService;
            _notificationService = notificationService;
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InitiatePayment(int bookingId, string email, decimal amount)
        {
            try
            {
                var result = await _paymentService.InitializePayment(email, amount, bookingId);

                if (!result.success)
                {
                    TempData["Error"] = $"Payment initialization failed: {result.message}";
                    return RedirectToAction("CheckoutDeposit", "Booking", new { id = bookingId });
                }

                if (string.IsNullOrEmpty(result.authorizationUrl))
                {
                    Console.WriteLine($"Paystack returned success but authorizationUrl is null/empty. Message: {result.message}");
                    TempData["Error"] = "Payment gateway returned an invalid response. Please try again.";
                    return RedirectToAction("CheckoutDeposit", "Booking", new { id = bookingId });
                }

                return Redirect(result.authorizationUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"InitiatePayment Exception: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                TempData["Error"] = $"An error occurred: {ex.Message}";
                return RedirectToAction("CheckoutDeposit", "Booking", new { id = bookingId });
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

            // 1️⃣ Verify with Paystack
            var result = await _paymentService.VerifyPayment(refToVerify);

            // 2️⃣ If verification fails, show error
            if (!result.success)
            {
                TempData["Error"] = $"Payment verification failed: {result.message}";
                return RedirectToAction("MyBookings", "Booking");
            }

            // 3️⃣ If data is null, show error
            if (result.data == null)
            {
                TempData["Error"] = "Payment verification returned no data.";
                return RedirectToAction("MyBookings", "Booking");
            }

            // 4️⃣ Only proceed if payment was successful
            if (result.data.status != "success")
            {
                TempData["Error"] = $"Payment was not successful. Status: {result.data.status}";
                return RedirectToAction("MyBookings", "Booking");
            }

            // 5️⃣ Find the payment record (with null‑safe access)
            var payment = await _context.Payments
                .Include(p => p.Booking)
                .FirstOrDefaultAsync(p => p.Reference == refToVerify);

            if (payment == null)
            {
                TempData["Error"] = "Payment record not found.";
                return RedirectToAction("MyBookings", "Booking");
            }

            // 6️⃣ Update payment and booking (only if still pending)
            if (payment.Status == "pending")
            {
                payment.Status = "success";
                payment.PaidAt = DateTime.UtcNow;
                payment.PaymentMethod = result.data.channel;  // safe because data is not null

                var booking = payment.Booking;
                if (booking != null && !booking.IsDepositPaid)
                {
                    booking.IsDepositPaid = true;
                    booking.Status = BookingStatus.Confirmed;
                    await _context.SaveChangesAsync();

                    // Send notifications...
                    var currentUser = await _userManager.FindByIdAsync(booking.CustomerId);
                    var artist = await _userManager.FindByIdAsync(booking.UserService.ArtistId);

                    await _notificationService.CreateNotificationAsync(
                        booking.CustomerId,
                        "Payment Received! 💰",
                        $"Your deposit of R{payment.Amount:N2} has been received. Appointment CONFIRMED!",
                        "payment_received",
                        booking.Id.ToString(),
                        Url.Action("MyBookings", "Booking")
                    );

                    await _notificationService.CreateNotificationAsync(
                        artist.Id,
                        "Deposit Paid! 🎉",
                        $"{currentUser.FirstName} paid deposit. Appointment confirmed.",
                        "payment_received",
                        booking.Id.ToString(),
                        Url.Action("MyAppointments", "Artist")
                    );

                    TempData["Success"] = "Payment successful! Your appointment is now confirmed.";
                }
                else
                {
                    TempData["Success"] = "Payment verified, but booking already confirmed.";
                }
            }
            else
            {
                TempData["Success"] = "Payment already verified.";
            }

            return RedirectToAction("MyBookings", "Booking");
        }


    }
}