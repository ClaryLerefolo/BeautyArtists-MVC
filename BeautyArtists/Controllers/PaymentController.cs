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

                // 3️⃣ If payment already processed, ensure booking is confirmed
                if (payment.Status == "success")
                {
                    var existingBooking = payment.Booking;
                    if (existingBooking != null && !existingBooking.IsDepositPaid)
                    {
                        // In case the booking wasn't confirmed (shouldn't happen, but just in case)
                        existingBooking.IsDepositPaid = true;
                        existingBooking.Status = BookingStatus.Confirmed;
                        await _context.SaveChangesAsync();

                        // Send notifications (if not already sent)
                        try
                        {
                            var currentUser = await _userManager.FindByIdAsync(existingBooking.CustomerId);
                            var artist = await _userManager.FindByIdAsync(existingBooking.UserService?.ArtistId);

                            if (currentUser != null)
                            {
                                await _notificationService.CreateNotificationAsync(
                                    existingBooking.CustomerId,
                                    "Payment Received! 💰",
                                    $"Your deposit of R{payment.Amount:N2} has been received. Appointment CONFIRMED!",
                                    "payment_received",
                                    existingBooking.Id.ToString(),
                                    Url.Action("MyBookings", "Booking")
                                );
                            }

                            if (artist != null)
                            {
                                await _notificationService.CreateNotificationAsync(
                                    artist.Id,
                                    "Deposit Paid! 🎉",
                                    $"{currentUser?.FirstName ?? "A client"} paid deposit. Appointment confirmed.",
                                    "payment_received",
                                    existingBooking.Id.ToString(),
                                    Url.Action("MyAppointments", "Artist")
                                );
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Notification error (non-critical): {ex.Message}");
                        }

                        TempData["Success"] = "Payment successful! Your appointment is now confirmed.";
                        return RedirectToAction("MyBookings", "Booking");
                    }

                    TempData["Success"] = "Payment already verified and booking confirmed.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                // 4️⃣ Update payment record (fresh payment)
                payment.Status = "success";
                payment.PaidAt = DateTime.UtcNow;
                payment.PaymentMethod = result.data.channel;

                // 5️⃣ Update booking
                var booking = payment.Booking;
                bool bookingUpdated = false;

                if (booking != null && !booking.IsDepositPaid)
                {
                    booking.IsDepositPaid = true;
                    booking.Status = BookingStatus.Confirmed;
                    bookingUpdated = true;
                }

                await _context.SaveChangesAsync();

                // 6️⃣ Send notifications (only if booking was just updated)
                if (bookingUpdated && booking != null)
                {
                    try
                    {
                        var currentUser = await _userManager.FindByIdAsync(booking.CustomerId);
                        var artist = await _userManager.FindByIdAsync(booking.UserService?.ArtistId);

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
                                $"{currentUser?.FirstName ?? "A client"} paid deposit. Appointment confirmed.",
                                "payment_received",
                                booking.Id.ToString(),
                                Url.Action("MyAppointments", "Artist")
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Notification error (non-critical): {ex.Message}");
                    }

                    TempData["Success"] = "Payment successful! Your appointment is now confirmed.";
                }
                else
                {
                    TempData["Success"] = "Payment verified and booking confirmed.";
                }

                return RedirectToAction("MyBookings", "Booking");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PaymentCallback error: {ex.Message}");
                Console.WriteLine($"Stack: {ex.StackTrace}");

                // If the booking was already updated, show success anyway
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