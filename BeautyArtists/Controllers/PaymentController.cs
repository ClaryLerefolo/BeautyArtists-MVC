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
                // 🔥 Prevent duplicate payment for already confirmed booking
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
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InitiateFinalPayment(int bookingId, string email, decimal amount)
        {
            try
            {
                // Check if booking exists and belongs to current user
                var booking = await _context.Bookings
                    .FirstOrDefaultAsync(b => b.Id == bookingId && b.CustomerId == _userManager.GetUserId(User));

                if (booking == null)
                {
                    TempData["Error"] = "Booking not found.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                // Check if booking is confirmed
                if (booking.Status != BookingStatus.Confirmed)
                {
                    TempData["Error"] = "Booking must be confirmed before final payment.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                // Check if already fully paid
                if (booking.TotalAmount == 0)
                {
                    TempData["Error"] = "This booking has already been fully paid.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                // Calculate remaining balance
                decimal remainingBalance = booking.TotalAmount / 2;

                if (remainingBalance <= 0)
                {
                    TempData["Error"] = "No remaining balance to pay.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                // Check if at least 2 days before appointment
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
        // Inside PaymentController.cs, update the PaymentCallback method

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
                // 1️⃣ Verify with Paystack
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

                // 2️⃣ Find the payment record
                var payment = await _context.Payments
                    .Include(p => p.Booking)
                    .FirstOrDefaultAsync(p => p.Reference == refToVerify);

                if (payment == null)
                {
                    TempData["Error"] = "Payment record not found.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                // 3️⃣ If payment already processed, ensure booking is confirmed/paid
                if (payment.Status == "success")
                {
                    var existingBooking = payment.Booking;
                    if (existingBooking != null)
                    {
                        // If booking is not confirmed yet, confirm it
                        if (!existingBooking.IsDepositPaid)
                        {
                            existingBooking.IsDepositPaid = true;
                            existingBooking.Status = BookingStatus.Confirmed;
                            await _context.SaveChangesAsync();

                            // Send notifications (deposit paid)
                            // ... notification code ...
                            TempData["Success"] = "Payment successful! Your appointment is now confirmed.";
                            return RedirectToAction("MyBookings", "Booking");
                        }

                        // If this is a final payment and TotalAmount > 0, mark as fully paid
                        if (existingBooking.TotalAmount > 0)
                        {
                            existingBooking.TotalAmount = 0;
                            await _context.SaveChangesAsync();

                            // Send final payment notifications
                            // ... notification code ...
                            TempData["Success"] = "Final payment successful! Your appointment is now fully paid.";
                            return RedirectToAction("MyBookings", "Booking");
                        }
                    }

                    TempData["Success"] = "Payment already verified.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                // 4️⃣ Update payment record (fresh payment)
                payment.Status = "success";
                payment.PaidAt = DateTime.UtcNow;
                payment.PaymentMethod = result.data.channel;

                // 5️⃣ Determine if this is deposit or final payment
                var booking = payment.Booking;
                bool isDeposit = !booking.IsDepositPaid;

                if (isDeposit)
                {
                    // Deposit payment
                    booking.IsDepositPaid = true;
                    booking.Status = BookingStatus.Confirmed;
                }
                else
                {
                    // Final payment - set TotalAmount to 0
                    booking.TotalAmount = 0;
                }

                await _context.SaveChangesAsync();

                // 6️⃣ Send notifications
                // ... notification code ...

                TempData["Success"] = isDeposit ? "Deposit successful! Appointment confirmed." : "Final payment successful! Appointment fully paid.";
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
    }
}
