using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
            var result = await _paymentService.InitializePayment(email, amount, bookingId);

            if (result.success)
            {
                return Redirect(result.authorizationUrl);
            }

            TempData["Error"] = $"Payment initialization failed: {result.message}";
            return RedirectToAction("CheckoutDeposit", "Booking", new { id = bookingId });
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

            var result = await _paymentService.VerifyPayment(refToVerify);

            if (result.success && result.data.status == "success")
            {
                var payment = await _context.Payments
                    .Include(p => p.Booking)
                    .FirstOrDefaultAsync(p => p.Reference == refToVerify);

                if (payment != null && payment.Status == "pending")
                {
                    payment.Status = "success";
                    payment.PaidAt = DateTime.UtcNow;

                    var booking = payment.Booking;
                    booking.IsDepositPaid = true;
                    booking.Status = BookingStatus.Confirmed;
                    await _context.SaveChangesAsync();

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
                    TempData["Success"] = "Payment verified successfully!";
                }
            }
            else
            {
                TempData["Error"] = $"Payment verification failed: {result.message}";
            }

            return RedirectToAction("MyBookings", "Booking");
        }
    }
}