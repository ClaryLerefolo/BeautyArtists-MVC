using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Models.ViewModels;
using BeautyArtists.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using static BeautyArtists.Models.Booking;

namespace BeautyArtists.Controllers
{
    public class BookingController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ICommunicationService _commService;
        private readonly INotificationService _notificationService;
        private const decimal COMMISSION_RATE = 0.15m;
        private const decimal BOOKING_FEE = 5.00m;

        public BookingController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, ICommunicationService commService, INotificationService notificationService)
        {
            _context = context;
            _userManager = userManager;
            _commService = commService;
            _notificationService = notificationService;
        }

        // ══════════════════════════════════
        //  GET: Booking/Book
        // ══════════════════════════════════
        public async Task<IActionResult> Book()
        {
            var services = await _context.UserServices
                .Include(us => us.Service)
                .Include(us => us.Artist)
                    .ThenInclude(a => a.ArtistProfile)
                .AsNoTracking()
                .ToListAsync();

            return View(services);
        }

        // ═══════════════════════════════════
        //  GET: Booking/GetArtistAvailability
        // ═══════════════════════════════════
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetArtistAvailability(string artistId)
        {
            if (string.IsNullOrEmpty(artistId))
            {
                return Json(new List<object>());
            }

            var today = DateTime.Now.Date;

            var slots = await _context.ArtistAvailabilities
                .Where(a => a.ArtistId == artistId
                    && !a.IsBooked
                    && a.AvailableDate >= today)
                .OrderBy(a => a.AvailableDate)
                .ThenBy(a => a.StartTime)
                .Select(a => new
                {
                    id = a.Id,
                    date = a.AvailableDate.ToString("yyyy-MM-dd"),
                    timeString = $"{a.StartTime:hh\\:mm} - {a.EndTime:hh\\:mm}"
                })
                .AsNoTracking()
                .ToListAsync();

            return Json(slots);
        }

        // ═══════════════════════════════════
        //  DEBUG: Check slots in database (REMOVE AFTER TESTING)
        // ═══════════════════════════════════
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> DebugSlots(string artistId)
        {
            if (string.IsNullOrEmpty(artistId))
                return Json(new { error = "No artistId provided" });

            var allSlots = await _context.ArtistAvailabilities
                .Where(a => a.ArtistId == artistId)
                .Select(a => new
                {
                    a.Id,
                    a.AvailableDate,
                    a.StartTime,
                    a.EndTime,
                    a.IsBooked,
                    IsFuture = a.AvailableDate >= DateTime.Now.Date,
                    CurrentDate = DateTime.Now.Date
                })
                .AsNoTracking()
                .ToListAsync();

            var availableSlots = allSlots.Where(s => !s.IsBooked && s.AvailableDate >= DateTime.Now.Date).ToList();

            return Json(new
            {
                artistId = artistId,
                totalSlots = allSlots.Count,
                availableSlots = availableSlots.Count,
                allSlots = allSlots,
                message = availableSlots.Count == 0 ? "NO AVAILABLE SLOTS FOUND! Please add availability as an artist." : "Slots found!"
            });
        }

        // ══════════════════════════════════
        //  GET: Booking/BookService
        // ══════════════════════════════════
        [Authorize]
        public async Task<IActionResult> BookService(int userServiceId)
        {
            var userService = await _context.UserServices
                .Include(us => us.Service)
                    .ThenInclude(s => s.ServiceCategory)
                .Include(us => us.Artist)
                    .ThenInclude(a => a.ArtistProfile)
                .AsNoTracking()
                .FirstOrDefaultAsync(us => us.Id == userServiceId);

            if (userService == null) return NotFound();

            var artistName = !string.IsNullOrEmpty(userService.Artist?.FirstName)
                ? $"{userService.Artist.FirstName} {userService.Artist.LastName}".Trim()
                : userService.Artist?.UserName ?? "Pro Artist";

            decimal bookingFee = BOOKING_FEE;
            decimal clientTotal = userService.Price + bookingFee;

            var model = new BookingViewModel
            {
                UserServiceId = userServiceId,
                ServiceName = userService.Service?.Name,
                Price = userService.Price,
                BookingFee = bookingFee,
                ClientTotal = clientTotal,
                ArtistName = artistName,
                ArtistId = userService.ArtistId,
                ArtistProfilePicture = userService.Artist?.ArtistProfile?.ProfilePictureUrl ?? "/images/default-profile.png",
                CategoryName = userService.Service?.ServiceCategory?.Name,
                SelectedLocationType = LocationType.WalkIn
            };

            return View("BookService", model);
        }

        // ══════════════════════════════════
        //  POST: Booking/ConfirmBooking
        // ══════════════════════════════════
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmBooking(BookingViewModel model)
        {
            try
            {
                if (!User.Identity.IsAuthenticated)
                    return Challenge();

                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null) return Challenge();

                // ── HOUSE CALL VALIDATION ──
                if (model.SelectedLocationType == LocationType.HouseCall)
                {
                    if (string.IsNullOrWhiteSpace(model.HouseNumber))
                        ModelState.AddModelError("HouseNumber", "House/Unit number is required for house calls.");

                    if (string.IsNullOrWhiteSpace(model.StreetAddress))
                        ModelState.AddModelError("StreetAddress", "Street address is required for house calls.");

                    if (string.IsNullOrWhiteSpace(model.AreaCode))
                        ModelState.AddModelError("AreaCode", "Area/Postal code is required for house calls.");

                    if (string.IsNullOrEmpty(model.Latitude) || string.IsNullOrEmpty(model.Longitude))
                        ModelState.AddModelError(string.Empty, "Please pin your exact location on the map.");
                }

                // ── IF VALIDATION FAILS ──
                if (!ModelState.IsValid)
                {
                    var userService = await _context.UserServices
                        .Include(us => us.Service)
                            .ThenInclude(s => s.ServiceCategory)
                        .Include(us => us.Artist)
                            .ThenInclude(a => a.ArtistProfile)
                        .AsNoTracking()
                        .FirstOrDefaultAsync(us => us.Id == model.UserServiceId);

                    if (userService != null)
                    {
                        model.ServiceName = userService.Service?.Name;
                        model.Price = userService.Price;
                        model.BookingFee = BOOKING_FEE;
                        model.ClientTotal = userService.Price + BOOKING_FEE;
                        model.ArtistId = userService.ArtistId;
                        model.ArtistName = !string.IsNullOrEmpty(userService.Artist?.FirstName)
                            ? $"{userService.Artist.FirstName} {userService.Artist.LastName}".Trim()
                            : userService.Artist?.UserName ?? "Pro Artist";
                        model.ArtistProfilePicture = userService.Artist?.ArtistProfile?.ProfilePictureUrl ?? "/images/default-profile.png";
                        model.CategoryName = userService.Service?.ServiceCategory?.Name;
                    }
                    return View("BookService", model);
                }

                // ── FETCH SLOT ──
                var slot = await _context.ArtistAvailabilities
                    .FirstOrDefaultAsync(a => a.Id == model.AvailabilitySlotId && !a.IsBooked);

                if (slot == null)
                {
                    ModelState.AddModelError(string.Empty, "Sorry, this slot was just booked by someone else. Please select another.");

                    var userService = await _context.UserServices
                        .Include(us => us.Service)
                            .ThenInclude(s => s.ServiceCategory)
                        .Include(us => us.Artist)
                            .ThenInclude(a => a.ArtistProfile)
                        .AsNoTracking()
                        .FirstOrDefaultAsync(us => us.Id == model.UserServiceId);

                    if (userService != null)
                    {
                        model.ServiceName = userService.Service?.Name;
                        model.Price = userService.Price;
                        model.BookingFee = BOOKING_FEE;
                        model.ClientTotal = userService.Price + BOOKING_FEE;
                        model.ArtistId = userService.ArtistId;
                        model.ArtistName = !string.IsNullOrEmpty(userService.Artist?.FirstName)
                            ? $"{userService.Artist.FirstName} {userService.Artist.LastName}".Trim()
                            : userService.Artist?.UserName ?? "Pro Artist";
                        model.ArtistProfilePicture = userService.Artist?.ArtistProfile?.ProfilePictureUrl ?? "/images/default-profile.png";
                        model.CategoryName = userService.Service?.ServiceCategory?.Name;
                    }
                    return View("BookService", model);
                }

                var appointmentDate = slot.AvailableDate.Add(slot.StartTime);

                // ── BUILD THE COMBINED ADDRESS ──
                string fullAddress = string.Empty;
                if (model.SelectedLocationType == LocationType.HouseCall)
                {
                    var parts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(model.HouseNumber)) parts.Add(model.HouseNumber);
                    if (!string.IsNullOrWhiteSpace(model.StreetAddress)) parts.Add(model.StreetAddress);
                    if (!string.IsNullOrWhiteSpace(model.AreaCode)) parts.Add(model.AreaCode);
                    fullAddress = string.Join(", ", parts);
                }

                // ── CALCULATE FEES & SPLITS ──
                decimal bookingFee = BOOKING_FEE;
                decimal servicePrice = model.Price;
                decimal clientTotal = servicePrice + bookingFee;
                decimal platformCommission = servicePrice * COMMISSION_RATE;
                decimal platformEarnings = bookingFee + platformCommission;
                decimal artistNetAmount = servicePrice * (1 - COMMISSION_RATE);

                // ── CREATE BOOKING ──
                var booking = new Booking
                {
                    CustomerId = currentUser.Id,
                    UserServiceId = model.UserServiceId,
                    BookingDate = DateTime.UtcNow,
                    AppointmentDate = appointmentDate,
                    Notes = model.Notes ?? "",
                    HasRescheduled = false,
                    Status = BookingStatus.Pending,
                    SelectedLocationType = model.SelectedLocationType.GetValueOrDefault(LocationType.WalkIn),
                    TransportCost = 0,

                    TotalAmount = clientTotal,
                    ServicePrice = servicePrice,
                    BookingFee = bookingFee,
                    PlatformCommission = platformCommission,
                    PlatformEarnings = platformEarnings,
                    ArtistNetAmount = artistNetAmount,
                    ArtistTotalEarned = 0m,

                    IsDepositPaid = false,
                    AvailabilitySlotId = slot.Id,

                    HouseNumber = model.HouseNumber ?? "",
                    StreetAddress = model.StreetAddress ?? "",
                    AreaCode = model.AreaCode ?? "",
                    HouseCallAddress = fullAddress,
                    Latitude = model.Latitude ?? "",
                    Longitude = model.Longitude ?? "",
                };

                _context.Bookings.Add(booking);
                await _context.SaveChangesAsync();

                slot.IsBooked = true;
                await _context.SaveChangesAsync();

                // ─── SEND NOTIFICATIONS & EMAILS ───
                try
                {
                    var serviceName = await _context.Services
                        .Where(s => s.Id == model.UserServiceId)
                        .Select(s => s.Name)
                        .AsNoTracking()
                        .FirstOrDefaultAsync() ?? "your service";

                    await _notificationService.CreateNotificationAsync(
                        slot.ArtistId,
                        "New Booking Request! 📅",
                        $"{currentUser.FirstName} has requested {serviceName} on {appointmentDate:MMM dd} at {appointmentDate:hh:mm tt}",
                        "booking_pending",
                        booking.Id.ToString(),
                        Url.Action("MyAppointments", "Artist")
                    );

                    await _notificationService.CreateNotificationAsync(
                        currentUser.Id,
                        "Booking Request Sent! 📤",
                        $"Your request for {serviceName} has been sent. You'll be notified when the artist responds.",
                        "booking_pending",
                        booking.Id.ToString(),
                        Url.Action("MyBookings", "Booking")
                    );

                    if (!string.IsNullOrEmpty(slot.ArtistId))
                    {
                        var artist = await _userManager.FindByIdAsync(slot.ArtistId);
                        if (artist != null && !string.IsNullOrEmpty(artist.Email))
                        {
                            await _commService.SendBookingRequestToArtistAsync(slot.ArtistId, booking.Id);
                        }
                    }

                    if (!string.IsNullOrEmpty(currentUser.Email))
                    {
                        try
                        {
                            await _commService.SendBookingConfirmationToClientAsync(currentUser.Id, booking.Id);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"❌ Failed to send client confirmation email: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Notification/Email error: {ex.Message}");
                }

                TempData["Success"] = booking.SelectedLocationType == LocationType.WalkIn
                    ? "Appointment requested successfully! The Artist must review and accept your slot before deposit payment can be processed."
                    : "House Call request sent! The Artist will review your location coordinates, apply any relevant transport costs, and accept.";

                return RedirectToAction("MyBookings");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FATAL ERROR in ConfirmBooking: {ex.Message}");
                TempData["Error"] = "There was an error processing your booking. Please try again.";
                return RedirectToAction("BookService", new { userServiceId = model.UserServiceId });
            }
        }

        // ══════════════════════════════════
        //  POST: Booking/ArtistUpdateStatus
        // ══════════════════════════════════
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ArtistUpdateStatus(int bookingId, Booking.BookingStatus newStatus, string artistNotes, decimal transportCost)
        {
            var booking = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.Customer)
                .FirstOrDefaultAsync(b => b.Id == bookingId);

            if (booking == null) return NotFound();

            booking.ArtistNotes = artistNotes;
            var client = await _userManager.FindByIdAsync(booking.CustomerId);

            if (newStatus == Booking.BookingStatus.Accepted)
            {
                if (booking.SelectedLocationType == LocationType.HouseCall)
                {
                    if (transportCost >= 0)
                    {
                        booking.TransportCost = transportCost;
                        booking.TotalAmount = booking.ServicePrice + transportCost + booking.BookingFee;
                    }
                }

                booking.Status = BookingStatus.Accepted;
                await _context.SaveChangesAsync();

                var depositUrl = Url.Action("CheckoutDeposit", "Booking", new { id = booking.Id }, Request.Scheme);

                // ─── CORRECT PAYMENT BREAKDOWN ───
                decimal serviceHalf = booking.ServicePrice / 2;
                decimal depositAmount = serviceHalf + booking.BookingFee; // 50% service + full booking fee
                decimal finalPayment = serviceHalf; // remaining 50% of service

                string subject = "✅ Your Appointment Has Been Accepted!";
                string emailBody = $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #f0c808; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
    <div style='text-align: center; margin-bottom: 20px;'>
        <h1 style='color: #f0c808; margin: 0;'>✨ Appointment Accepted! ✨</h1>
        <hr style='border-color: #f0c808;'>
    </div>
    
    <p style='font-size: 16px;'>Dear <strong>{booking.Customer?.FirstName} {booking.Customer?.LastName}</strong>,</p>
    
    <p style='font-size: 14px; color: #ddd;'>Great news! The artist has <strong style='color: #28a745;'>ACCEPTED</strong> your appointment request.</p>
    
    <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
        <h3 style='color: #f0c808; margin-top: 0;'>📋 Booking Details</h3>
        <p><strong>Service:</strong> {booking.UserService?.Service?.Name}</p>
        <p><strong>Artist:</strong> {booking.UserService?.Artist?.FirstName} {booking.UserService?.Artist?.LastName}</p>
        <p><strong>Date:</strong> {booking.AppointmentDate.ToString("dddd, MMMM dd, yyyy")}</p>
        <p><strong>Time:</strong> {booking.AppointmentDate.ToString("hh:mm tt")}</p>
        <p><strong>Location Type:</strong> {(booking.SelectedLocationType == LocationType.HouseCall ? "🏠 House Call" : "🏢 Walk-In")}</p>
        {(booking.SelectedLocationType == LocationType.HouseCall && !string.IsNullOrEmpty(booking.HouseCallAddress) ? $"<p><strong>📍 Address:</strong> {booking.HouseCallAddress}</p>" : "")}
    </div>
    
    <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
        <h3 style='color: #f0c808; margin-top: 0;'>💰 Payment Breakdown</h3>
        <p><strong>Service Price:</strong> R {booking.ServicePrice:N2}</p>
        {(booking.TransportCost > 0 ? $"<p><strong>Transport Cost:</strong> R {booking.TransportCost:N2}</p>" : "")}
        <p><strong>Booking Fee (one-time):</strong> R {booking.BookingFee:N2}</p>
        <p><strong>Total Amount:</strong> <span style='color: #f0c808; font-size: 18px;'>R {booking.TotalAmount:N2}</span></p>
        <hr style='border-color: #333;'>
        <p><strong>Deposit Required (50% of service + R5 booking fee):</strong> <span style='color: #ff6600;'>R {depositAmount:N2}</span></p>
        <p><strong>Final Payment (remaining 50% of service):</strong> R {finalPayment:N2}</p>
    </div>
    
    {(booking.ArtistNotes != null ? $@"
    <div style='background: rgba(240, 200, 8, 0.1); padding: 15px; border-radius: 8px; margin: 15px 0; border-left: 4px solid #f0c808;'>
        <p style='margin: 0;'><strong>📝 Message from your artist:</strong></p>
        <p style='margin: 5px 0 0 0; color: #ddd; font-style: italic;'>“{booking.ArtistNotes}”</p>
    </div>" : "")}
    
    <div style='text-align: center; margin: 25px 0;'>
        <a href='{depositUrl}' style='background: linear-gradient(45deg, #f0c808, #e50914); color: #000; padding: 14px 30px; text-decoration: none; border-radius: 50px; font-weight: bold; font-size: 16px; display: inline-block;'>
            💰 PAY YOUR DEPOSIT NOW
        </a>
    </div>
    
    <div style='background: rgba(229, 9, 20, 0.1); padding: 12px; border-radius: 8px; margin: 15px 0; border-left: 4px solid #e50914;'>
        <p style='margin: 0; font-size: 12px; color: #ff8888;'>
            <strong>⚠️ IMPORTANT:</strong> Your appointment is not confirmed until the deposit is paid. Please complete your payment as soon as possible.
        </p>
    </div>
    
    <hr style='border-color: #333; margin: 20px 0;'>
    <p style='font-size: 11px; color: #666; text-align: center;'>
        Need to reschedule or cancel? Please contact the artist directly through your dashboard.<br>
        &copy; {DateTime.Now.Year} RubiOr
    </p>
</div>";

                if (client != null && !string.IsNullOrEmpty(client.Email))
                {
                    await _commService.SendDirectMessageEmailAsync(
                        booking.UserService?.ArtistId,
                        booking.CustomerId,
                        subject,
                        emailBody
                    );
                }

                await _notificationService.CreateNotificationAsync(
                    booking.CustomerId,
                    "Appointment Accepted! ✅",
                    $"Great news! Your appointment for {booking.UserService?.Service?.Name} on {booking.AppointmentDate:MMM dd} has been ACCEPTED. Pay your deposit now!",
                    "booking_accepted",
                    booking.Id.ToString(),
                    Url.Action("CheckoutDeposit", "Booking", new { id = booking.Id })
                );

                await _notificationService.CreateNotificationAsync(
                    booking.UserService.ArtistId,
                    "You Accepted an Appointment 🎉",
                    $"You accepted {booking.Customer?.FirstName}'s appointment for {booking.UserService?.Service?.Name} on {booking.AppointmentDate:MMM dd}.",
                    "booking_accepted",
                    booking.Id.ToString(),
                    Url.Action("MyAppointments", "Artist")
                );

                TempData["Success"] = "Appointment accepted! Client has been notified to pay deposit.";
            }
            else if (newStatus == Booking.BookingStatus.Rejected)
            {
                booking.Status = BookingStatus.Rejected;
                await _context.SaveChangesAsync();

                string rejectSubject = "❌ Appointment Request Update";
                string rejectBody = $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #e50914; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
    <h2 style='color: #e50914; text-align: center;'>Appointment Not Accepted</h2>
    <p>Dear {booking.Customer?.FirstName},</p>
    <p>Unfortunately, your appointment request for <strong>{booking.UserService?.Service?.Name}</strong> on <strong>{booking.AppointmentDate:MMM dd, yyyy} at {booking.AppointmentDate:hh:mm tt}</strong> has been declined.</p>
    {(artistNotes != null ? $"<p><strong>Reason:</strong> {artistNotes}</p>" : "")}
    <p>Please try booking a different time slot or contact the artist directly.</p>
    <hr>
    <p style='font-size: 12px; color: #666;'>RubiOr</p>
</div>";

                if (client != null && !string.IsNullOrEmpty(client.Email))
                {
                    await _commService.SendDirectMessageEmailAsync(
                        booking.UserService?.ArtistId,
                        booking.CustomerId,
                        rejectSubject,
                        rejectBody
                    );
                }

                await _notificationService.CreateNotificationAsync(
                    booking.CustomerId,
                    "Appointment Declined ❌",
                    $"Unfortunately, your appointment request for {booking.UserService?.Service?.Name} on {booking.AppointmentDate:MMM dd} has been declined.",
                    "booking_rejected",
                    booking.Id.ToString(),
                    Url.Action("MyBookings", "Booking")
                );

                if (booking.AvailabilitySlotId.HasValue)
                {
                    var slot = await _context.ArtistAvailabilities.FirstOrDefaultAsync(a => a.Id == booking.AvailabilitySlotId.Value);
                    if (slot != null) slot.IsBooked = false;
                    await _context.SaveChangesAsync();
                }

                TempData["Success"] = "Appointment request rejected. Client has been notified.";
            }
            else if (newStatus == Booking.BookingStatus.Completed)
            {
                booking.Status = BookingStatus.Completed;
                await _context.SaveChangesAsync();

                string completeSubject = "🎉 Service Completed! Thank You!";
                string completeBody = $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #28a745; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
    <h2 style='color: #28a745; text-align: center;'>Service Completed! 🎉</h2>
    <p>Dear {booking.Customer?.FirstName},</p>
    <p>Your <strong>{booking.UserService?.Service?.Name}</strong> appointment has been marked as completed.</p>
    <p>We hope you had a great experience! Thank you for choosing RubiOr!</p>
    <p style='text-align: center; margin-top: 20px;'>✨ We hope to see you again soon! ✨</p>
</div>";

                if (client != null && !string.IsNullOrEmpty(client.Email))
                {
                    await _commService.SendDirectMessageEmailAsync(
                        booking.UserService?.ArtistId,
                        booking.CustomerId,
                        completeSubject,
                        completeBody
                    );
                }

                await _notificationService.CreateNotificationAsync(
                    booking.CustomerId,
                    "Service Completed! ⭐",
                    $"Your {booking.UserService?.Service?.Name} appointment has been completed. Thank you for choosing us!",
                    "booking_completed",
                    booking.Id.ToString(),
                    Url.Action("MyBookings", "Booking")
                );

                TempData["Success"] = "Service marked as completed! Client has been notified.";
            }

            return RedirectToAction("MyAppointments", "Artist");
        }

        // ══════════════════════════════════
        //  GET: Booking/CheckoutDeposit
        // ══════════════════════════════════
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> CheckoutDeposit(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return Challenge();

            var booking = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService.Artist)
                    .ThenInclude(a => a.ArtistProfile)
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id && b.CustomerId == currentUser.Id);

            if (booking == null) return NotFound();

            if (booking.Status != BookingStatus.Accepted)
            {
                TempData["Error"] = "This booking must be accepted by the artist before payment.";
                return RedirectToAction("MyBookings");
            }

            if (booking.IsDepositPaid)
            {
                TempData["Error"] = "Deposit already paid for this booking.";
                return RedirectToAction("MyBookings");
            }

            var daysUntilAppointment = (booking.AppointmentDate.Date - DateTime.Now.Date).TotalDays;
            var isLastMinute = daysUntilAppointment < 2;

            var depositAmount = isLastMinute ? booking.TotalAmount : (booking.ServicePrice / 2) + booking.BookingFee;

            var model = new CheckoutViewModel
            {
                Booking = booking,
                DepositAmount = depositAmount,
                UserEmail = currentUser.Email,
                UserName = $"{currentUser.FirstName} {currentUser.LastName}",
                IsLastMinute = isLastMinute
            };

            return View(model);
        }

        // ══════════════════════════════════
        //  POST: Booking/ProcessDeposit
        // ══════════════════════════════════
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessDeposit(int id, string paymentReference)
        {
            try
            {
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null) return Challenge();

                var booking = await _context.Bookings
                    .Include(b => b.UserService)
                        .ThenInclude(us => us.Artist)
                    .Include(b => b.UserService.Service)
                    .FirstOrDefaultAsync(b => b.Id == id && b.CustomerId == currentUser.Id);

                if (booking == null)
                    return NotFound();

                if (booking.IsDepositPaid)
                {
                    TempData["Error"] = "Deposit already paid.";
                    return RedirectToAction("MyBookings");
                }

                decimal depositAmount = (booking.ServicePrice / 2) + booking.BookingFee;
                decimal artistShareOfDeposit = (booking.ServicePrice / 2) * (1 - COMMISSION_RATE);

                booking.DepositPaid = depositAmount;
                booking.DepositPaidDate = DateTime.UtcNow;
                booking.IsDepositPaid = true;
                booking.ArtistTotalEarned = artistShareOfDeposit;
                await _context.SaveChangesAsync();

                // ── Send notifications ──
                var artist = booking.UserService?.Artist;
                var serviceName = booking.UserService?.Service?.Name ?? "your service";

                await _notificationService.CreateNotificationAsync(
                    booking.UserService.ArtistId,
                    "Deposit Received! 💰",
                    $"{currentUser.FirstName} has paid the deposit for {serviceName}.",
                    "deposit_paid",
                    booking.Id.ToString(),
                    Url.Action("MyAppointments", "Artist")
                );

                if (artist != null && !string.IsNullOrEmpty(artist.Email))
                {
                    string artistSubject = "💰 Deposit Received!";
                    string artistBody = $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #f0c808; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
    <h2 style='color: #f0c808; text-align: center;'>Deposit Received! ✅</h2>
    <p>Dear {artist.FirstName},</p>
    <p>The client <strong>{currentUser.FirstName} {currentUser.LastName}</strong> has paid the deposit of <strong>R{depositAmount:N2}</strong> for:</p>
    <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
        <p><strong>Service:</strong> {serviceName}</p>
        <p><strong>Date:</strong> {booking.AppointmentDate:dddd, MMMM dd, yyyy}</p>
        <p><strong>Time:</strong> {booking.AppointmentDate:hh:mm tt}</p>
        <p><strong>Your Cut (85%):</strong> R {artistShareOfDeposit:N2}</p>
    </div>
    <p>The client will pay the remaining 50% of the service price 2 days before the appointment.</p>
    <hr>
    <p style='font-size: 12px; color: #666;'>RubiOr</p>
</div>";

                    await _commService.SendDirectMessageEmailAsync(currentUser.Id, artist.Id, artistSubject, artistBody);
                }

                string clientSubject = "✅ Deposit Confirmed!";
                string clientBody = $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #28a745; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
    <h2 style='color: #28a745; text-align: center;'>Deposit Confirmed! 🎉</h2>
    <p>Dear {currentUser.FirstName},</p>
    <p>Your deposit of <strong>R{depositAmount:N2}</strong> has been received.</p>
    <p><strong>Deposit Breakdown:</strong></p>
    <p style='padding-left: 20px;'>50% of Service Price: R {(booking.ServicePrice / 2):N2}</p>
    <p style='padding-left: 20px; color: #f0c808;'>+ Booking Fee: R {booking.BookingFee:N2}</p>
    <p>Your appointment for <strong>{serviceName}</strong> on <strong>{booking.AppointmentDate:dddd, MMMM dd, yyyy} at {booking.AppointmentDate:hh:mm tt}</strong> is now <strong>CONFIRMED</strong>.</p>
    <p>You will pay the remaining <strong>R {(booking.ServicePrice / 2):N2}</strong> 2 days before the appointment.</p>
    <hr>
    <p style='font-size: 12px; color: #666;'>RubiOr</p>
</div>";

                await _commService.SendDirectMessageEmailAsync(artist?.Id, currentUser.Id, clientSubject, clientBody);

                booking.Status = BookingStatus.Confirmed;
                await _context.SaveChangesAsync();

                TempData["Success"] = "Deposit paid! Your appointment is now confirmed.";
                return RedirectToAction("MyBookings");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ProcessDeposit error: {ex.Message}");
                TempData["Error"] = "An error occurred while processing your deposit. Please try again.";
                return RedirectToAction("MyBookings");
            }
        }

        // ══════════════════════════════════
        //  POST: Booking/ProcessFinalPayment
        // ══════════════════════════════════
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessFinalPayment(int id)
        {
            try
            {
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null) return Challenge();

                var booking = await _context.Bookings
                    .Include(b => b.UserService)
                        .ThenInclude(us => us.Artist)
                    .Include(b => b.UserService.Service)
                    .FirstOrDefaultAsync(b => b.Id == id && b.CustomerId == currentUser.Id);

                if (booking == null)
                {
                    TempData["Error"] = "Booking not found.";
                    return RedirectToAction("MyBookings");
                }

                if (!booking.IsDepositPaid)
                {
                    TempData["Error"] = "You must pay the initial deposit first.";
                    return RedirectToAction("MyBookings");
                }

                decimal remainingBalance = booking.ServicePrice / 2;

                if (remainingBalance <= 0)
                {
                    TempData["Error"] = "This booking has already been fully paid.";
                    return RedirectToAction("MyBookings");
                }

                double daysUntilAppointment = (booking.AppointmentDate.Date - DateTime.Now.Date).TotalDays;
                if (daysUntilAppointment < 2)
                {
                    TempData["Error"] = "Final payment must be cleared at least 2 days before the appointment.";
                    return RedirectToAction("MyBookings");
                }

                booking.FinalPaymentPaid = remainingBalance;
                booking.FinalPaidDate = DateTime.UtcNow;
                booking.ArtistTotalEarned += remainingBalance * (1 - COMMISSION_RATE);
                await _context.SaveChangesAsync();

                var artist = booking.UserService?.Artist;
                var serviceName = booking.UserService?.Service?.Name ?? "your service";

                await _notificationService.CreateNotificationAsync(
                    booking.UserService.ArtistId,
                    "Final Payment Received! 💵",
                    $"{currentUser.FirstName} has paid the remaining balance for {serviceName}.",
                    "payment_received",
                    booking.Id.ToString(),
                    Url.Action("MyAppointments", "Artist")
                );

                if (artist != null && !string.IsNullOrEmpty(artist.Email))
                {
                    string artistSubject = "💰 Final Payment Received – Appointment Fully Paid!";
                    string artistBody = $@"
    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #28a745; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
        <h2 style='color: #28a745; text-align: center;'>Final Payment Received! ✅</h2>
        <p>Dear {artist.FirstName},</p>
        <p>The client <strong>{currentUser.FirstName} {currentUser.LastName}</strong> has paid the remaining balance of <strong>R{remainingBalance:N2}</strong> for:</p>
        <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
            <p><strong>Service:</strong> {serviceName}</p>
            <p><strong>Date:</strong> {booking.AppointmentDate:dddd, MMMM dd, yyyy}</p>
            <p><strong>Time:</strong> {booking.AppointmentDate:hh:mm tt}</p>
            <p><strong>Your Total Cut (85%):</strong> R {booking.ArtistTotalEarned:N2}</p>
        </div>
        <p>This appointment is now <strong>FULLY PAID</strong>.</p>
        <hr>
        <p style='font-size: 12px; color: #666;'>RubiOr</p>
    </div>";

                    await _commService.SendDirectMessageEmailAsync(currentUser.Id, artist.Id, artistSubject, artistBody);
                }

                string clientSubject = "✅ Final Payment Confirmed!";
                string clientBody = $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #28a745; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
    <h2 style='color: #28a745; text-align: center;'>Final Payment Confirmed! 🎉</h2>
    <p>Dear {currentUser.FirstName},</p>
    <p>Your final payment of <strong>R{remainingBalance:N2}</strong> has been received.</p>
    <p>Your appointment for <strong>{serviceName}</strong> on <strong>{booking.AppointmentDate:dddd, MMMM dd, yyyy} at {booking.AppointmentDate:hh:mm tt}</strong> is now <strong>FULLY PAID</strong>.</p>
    <p>Thank you for choosing RubiOr!</p>
    <hr>
    <p style='font-size: 12px; color: #666;'>RubiOr</p>
</div>";

                await _commService.SendDirectMessageEmailAsync(artist?.Id, currentUser.Id, clientSubject, clientBody);

                TempData["Success"] = "Final payment cleared! Your appointment is now fully paid.";
                return RedirectToAction("MyBookings");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ProcessFinalPayment error: {ex.Message}");
                TempData["Error"] = "An error occurred while processing your payment. Please try again.";
                return RedirectToAction("MyBookings");
            }
        }

        // ══════════════════════════════════
        //  POST: Booking/Cancel
        // ══════════════════════════════════
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id, string? clientNotes)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return Challenge();

            var booking = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .FirstOrDefaultAsync(b => b.Id == id && b.CustomerId == currentUser.Id);

            if (booking == null ||
                booking.Status == BookingStatus.Completed ||
                booking.Status == BookingStatus.Cancelled)
                return NotFound();

            booking.Status = BookingStatus.Cancelled;
            booking.ClientNotes = clientNotes;

            if (booking.AvailabilitySlotId.HasValue)
            {
                var slot = await _context.ArtistAvailabilities
                    .FirstOrDefaultAsync(a => a.Id == booking.AvailabilitySlotId.Value);
                if (slot != null) slot.IsBooked = false;
            }

            await _context.SaveChangesAsync();

            try
            {
                if (booking.UserService != null && !string.IsNullOrEmpty(booking.UserService.ArtistId))
                {
                    await _commService.SendDirectMessageEmailAsync(
                        currentUser.Id,
                        booking.UserService.ArtistId,
                        "Booking Cancelled By Client",
                        $"Client {currentUser.FirstName} has cancelled Booking #{booking.Id} for {booking.UserService?.Service?.Name} on {booking.AppointmentDate:MMM dd}."
                    );
                }

                await _notificationService.CreateNotificationAsync(
                    booking.UserService.ArtistId,
                    "Booking Cancelled ❌",
                    $"{currentUser.FirstName} has cancelled their booking for {booking.UserService?.Service?.Name} on {booking.AppointmentDate:MMM dd}.",
                    "booking_cancelled",
                    booking.Id.ToString(),
                    Url.Action("MyAppointments", "Artist")
                );

                await _notificationService.CreateNotificationAsync(
                    currentUser.Id,
                    "Booking Cancelled ❌",
                    $"You have cancelled your appointment for {booking.UserService?.Service?.Name}.",
                    "booking_cancelled",
                    booking.Id.ToString(),
                    Url.Action("MyBookings", "Booking")
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Notification/Email failed for cancellation of booking {booking.Id}: {ex.Message}");
            }

            TempData["Success"] = "Your booking has been cancelled.";
            return RedirectToAction("MyBookings");
        }

        // ══════════════════════════════════
        //  GET: Booking/Reschedule
        // ══════════════════════════════════
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Reschedule(int id)
        {
            var currentUserId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(currentUserId)) return Challenge();

            var booking = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                        .ThenInclude(s => s.ServiceCategory)
                .Include(b => b.UserService.Artist)
                    .ThenInclude(a => a.ArtistProfile)
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id && b.CustomerId == currentUserId);

            if (booking == null || booking.HasRescheduled || booking.Status != BookingStatus.Confirmed)
                return NotFound();

            if (booking.AppointmentDate <= DateTime.Now.AddHours(24))
            {
                TempData["Error"] = "Rescheduling is only allowed at least 24 hours before your appointment.";
                return RedirectToAction("MyBookings");
            }

            string artistName = booking.UserService?.Artist?.ArtistProfile?.FullName
                ?? (!string.IsNullOrEmpty(booking.UserService?.Artist?.FirstName)
                    ? $"{booking.UserService.Artist.FirstName} {booking.UserService.Artist.LastName}".Trim()
                    : booking.UserService?.Artist?.UserName ?? "Pro Artist");

            var model = new BookingViewModel
            {
                BookingId = booking.Id,
                UserServiceId = booking.UserServiceId,
                PreferredDate = booking.AppointmentDate,
                Notes = booking.Notes,
                ServiceName = booking.UserService?.Service?.Name,
                ArtistName = artistName,
                ArtistId = booking.UserService?.ArtistId,
                Price = booking.ServicePrice,
                BookingFee = booking.BookingFee,
                ClientTotal = booking.TotalAmount
            };

            return View("Reschedule", model);
        }

        // ══════════════════════════════════
        //  POST: Booking/Reschedule
        // ══════════════════════════════════
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reschedule(BookingViewModel model)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return Challenge();

            var booking = await _context.Bookings
                .Include(b => b.UserService)
                .FirstOrDefaultAsync(b => b.Id == model.BookingId && b.CustomerId == currentUser.Id);

            if (booking == null || booking.Status != BookingStatus.Confirmed || booking.HasRescheduled)
                return NotFound();

            if (booking.AppointmentDate <= DateTime.Now.AddHours(24))
            {
                TempData["Error"] = "Rescheduling is only allowed at least 24 hours before your appointment.";
                return RedirectToAction("MyBookings");
            }

            var newSlot = await _context.ArtistAvailabilities
                .FirstOrDefaultAsync(a =>
                    a.Id == model.AvailabilitySlotId &&
                    a.ArtistId == booking.UserService.ArtistId &&
                    !a.IsBooked);

            if (newSlot == null)
            {
                TempData["Error"] = "That slot is no longer available. Please choose another.";
                return RedirectToAction("Reschedule", new { id = model.BookingId });
            }

            if (booking.AvailabilitySlotId.HasValue)
            {
                var oldSlot = await _context.ArtistAvailabilities
                    .FirstOrDefaultAsync(a => a.Id == booking.AvailabilitySlotId.Value);
                if (oldSlot != null) oldSlot.IsBooked = false;
            }

            booking.AppointmentDate = newSlot.AvailableDate.Add(newSlot.StartTime);
            booking.AvailabilitySlotId = newSlot.Id;
            booking.HasRescheduled = true;
            booking.ClientNotes = model.Notes;
            newSlot.IsBooked = true;

            await _context.SaveChangesAsync();

            await _notificationService.CreateNotificationAsync(
                booking.CustomerId,
                "Appointment Rescheduled 🔄",
                $"Your appointment for {booking.UserService?.Service?.Name} has been rescheduled to {newSlot.AvailableDate:MMM dd} at {newSlot.StartTime:hh\\:mm}.",
                "booking_rescheduled",
                booking.Id.ToString(),
                Url.Action("MyBookings", "Booking")
            );

            await _notificationService.CreateNotificationAsync(
                booking.UserService.ArtistId,
                "Appointment Rescheduled 🔄",
                $"{currentUser.FirstName} has rescheduled their appointment to {newSlot.AvailableDate:MMM dd} at {newSlot.StartTime:hh\\:mm}.",
                "booking_rescheduled",
                booking.Id.ToString(),
                Url.Action("MyAppointments", "Artist")
            );

            if (booking.UserService != null && !string.IsNullOrEmpty(booking.UserService.ArtistId))
            {
                await _commService.SendDirectMessageEmailAsync(
                    currentUser.Id,
                    booking.UserService.ArtistId,
                    "Appointment Date Rescheduled",
                    $"Client {currentUser.FirstName} has rescheduled to {booking.AppointmentDate:MMM dd, yyyy} at {newSlot.StartTime:hh\\:mm}."
                );
            }

            TempData["Success"] = $"Rescheduled to {newSlot.AvailableDate:MMM dd} at {newSlot.StartTime:hh\\:mm}!";
            return RedirectToAction("MyBookings");
        }

        // ══════════════════════════════════
        //  GET: Booking/CheckoutFinalPayment
        // ══════════════════════════════════
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> CheckoutFinalPayment(int id)
        {
            try
            {
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null) return Challenge();

                var booking = await _context.Bookings
                    .Include(b => b.UserService)
                        .ThenInclude(us => us.Service)
                    .Include(b => b.UserService.Artist)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.Id == id && b.CustomerId == currentUser.Id);

                if (booking == null)
                {
                    TempData["Error"] = "Booking not found.";
                    return RedirectToAction("MyBookings");
                }

                if (booking.Status != BookingStatus.Confirmed)
                {
                    TempData["Error"] = "This booking must be confirmed before final payment.";
                    return RedirectToAction("MyBookings");
                }

                decimal remainingBalance = booking.ServicePrice / 2;

                if (!booking.IsDepositPaid)
                {
                    TempData["Error"] = "Please pay the deposit first.";
                    return RedirectToAction("MyBookings");
                }

                if (booking.FinalPaymentPaid >= remainingBalance || remainingBalance <= 0)
                {
                    TempData["Error"] = "This booking has already been fully paid.";
                    return RedirectToAction("MyBookings");
                }

                double daysUntilAppointment = (booking.AppointmentDate.Date - DateTime.Now.Date).TotalDays;
                bool isLastMinute = daysUntilAppointment < 2;

                if (isLastMinute)
                {
                    decimal totalRemaining = booking.TotalAmount - booking.DepositPaid;
                    if (totalRemaining > 0)
                    {
                        remainingBalance = totalRemaining;
                    }
                }

                if (remainingBalance <= 0)
                {
                    TempData["Error"] = "This booking has already been fully paid.";
                    return RedirectToAction("MyBookings");
                }

                var model = new CheckoutViewModel
                {
                    Booking = booking,
                    DepositAmount = remainingBalance,
                    UserEmail = currentUser.Email,
                    UserName = $"{currentUser.FirstName} {currentUser.LastName}"
                };

                return View("CheckoutFinalPayment", model);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CheckoutFinalPayment error: {ex.Message}");
                TempData["Error"] = "An error occurred. Please try again.";
                return RedirectToAction("MyBookings");
            }
        }

        // ══════════════════════════════════
        //  GET: Booking/MyBookings
        // ══════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> MyBookings(
            int page = 1,
            int pageSize = 10,
            string month = null,
            DateTime? startDate = null,
            DateTime? endDate = null,
            string status = null)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return Challenge();

            // ─── BASE QUERY ──────────────────────────────────────────────
            var query = _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                        .ThenInclude(a => a.ArtistProfile)
                .Where(b => b.CustomerId == currentUser.Id && b.UserService != null)
                .AsNoTracking();

            // ─── APPLY FILTERS ──────────────────────────────────────────

            // Month filter (format "yyyy-MM")
            if (!string.IsNullOrEmpty(month) &&
                DateTime.TryParseExact(month, "yyyy-MM",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var monthDate))
            {
                var start = new DateTime(monthDate.Year, monthDate.Month, 1);
                var end = start.AddMonths(1).AddDays(-1);
                query = query.Where(b => b.AppointmentDate >= start && b.AppointmentDate <= end);
            }

            // Date range (start)
            if (startDate.HasValue)
                query = query.Where(b => b.AppointmentDate >= startDate.Value);

            // Date range (end)
            if (endDate.HasValue)
                query = query.Where(b => b.AppointmentDate <= endDate.Value);

            // Status filter
            if (!string.IsNullOrEmpty(status) &&
                Enum.TryParse<BookingStatus>(status, true, out var statusEnum))
            {
                query = query.Where(b => b.Status == statusEnum);
            }

            // ─── PAGINATION ──────────────────────────────────────────────

            var totalCount = await query.CountAsync();

            var bookings = await query
                .OrderByDescending(b => b.BookingDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // ─── REVIEW CHECK ────────────────────────────────────────────

            var bookingIds = bookings.Select(b => b.Id).ToList();
            var reviewedBookingIds = new List<int>();
            if (bookingIds.Any())
            {
                reviewedBookingIds = await _context.Reviews
                    .Where(r => bookingIds.Contains(r.BookingId))
                    .Select(r => r.BookingId)
                    .Distinct()
                    .ToListAsync();
            }

            // ─── BUILD VIEW MODEL ───────────────────────────────────────

            var model = new MyBookingsViewModel
            {
                Bookings = bookings.Select(b => new BookingWithReviewStatus
                {
                    Booking = b,
                    HasReviewed = reviewedBookingIds.Contains(b.Id),
                    StudioAddress = b.UserService?.Artist?.ArtistProfile?.StudioAddress,
                    StudioCity = b.UserService?.Artist?.ArtistProfile?.StudioCity,
                    StudioProvince = b.UserService?.Artist?.ArtistProfile?.StudioProvince,
                    StudioLatitude = b.UserService?.Artist?.ArtistProfile?.StudioLatitude,
                    StudioLongitude = b.UserService?.Artist?.ArtistProfile?.StudioLongitude
                }).ToList(),
                CurrentPage = page,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize),
                TotalCount = totalCount
            };

            // ─── PASS FILTER VALUES TO VIEWBAG (for UI) ──────────────

            ViewBag.SelectedMonth = month;
            ViewBag.StartDate = startDate;
            ViewBag.EndDate = endDate;
            ViewBag.SelectedStatus = status;

            return View(model);
        }
    }
}