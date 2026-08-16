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

        // ─── PRICING CONSTANTS ───
        private const decimal CLIENT_MARKUP_RATE = 0.04m;      // 4% card processing fee
        private const decimal BOOKING_FEE = 5.00m;              // Flat R5 booking fee
        private const decimal NEW_CLIENT_COMMISSION = 0.10m;   // 10% for new clients
        private const decimal REPEAT_CLIENT_FLAT_FEE = 15.00m; // R15 for repeat clients
        private const decimal MIN_PLATFORM_FEE = 8.00m;        // Minimum fee floor

        public BookingController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, ICommunicationService commService, INotificationService notificationService)
        {
            _context = context;
            _userManager = userManager;
            _commService = commService;
            _notificationService = notificationService;
        }

        // ─── HELPER: Check if client is new to this artist ───
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

        // ─── Card processing fee is ONLY 4% ───
        private decimal CalculateCardProcessingFee(decimal servicePrice)
        {
            return servicePrice * CLIENT_MARKUP_RATE;
        }

        // ─── Client total = price + card fee + booking fee ───
        private decimal CalculateClientTotal(decimal servicePrice)
        {
            return servicePrice + CalculateCardProcessingFee(servicePrice) + BOOKING_FEE;
        }

        // ─── Deposit = 50% of service price + card fee + booking fee ───
        private decimal CalculateDepositAmount(decimal servicePrice)
        {
            decimal halfService = servicePrice / 2;
            decimal cardFee = CalculateCardProcessingFee(servicePrice);
            return halfService + cardFee + BOOKING_FEE;
        }

        // ─── Final = 50% of service price (NO fees!) ───
        private decimal CalculateFinalAmount(decimal servicePrice)
        {
            return servicePrice / 2;
        }

        // ─── HELPER: Calculate artist payout ───
        private decimal CalculateArtistPayout(decimal artistPrice, bool isNewClient)
        {
            decimal platformFee = isNewClient
                ? artistPrice * NEW_CLIENT_COMMISSION
                : REPEAT_CLIENT_FLAT_FEE;

            platformFee = Math.Max(platformFee, MIN_PLATFORM_FEE);
            return artistPrice - platformFee;
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
        //  DEBUG: Check slots in database
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

            decimal servicePrice = userService.Price;
            decimal cardProcessingFee = CalculateCardProcessingFee(servicePrice);
            decimal clientTotal = CalculateClientTotal(servicePrice);

            var currentUser = await _userManager.GetUserAsync(User);
            bool isNewClient = currentUser != null
                ? await IsNewClient(currentUser.Id, userService.ArtistId)
                : true;

            var model = new BookingViewModel
            {
                UserServiceId = userServiceId,
                ServiceName = userService.Service?.Name,
                Price = servicePrice,
                CardProcessingFee = cardProcessingFee,
                BookingFee = BOOKING_FEE,
                ClientTotal = clientTotal,
                ArtistName = artistName,
                ArtistId = userService.ArtistId,
                ArtistProfilePicture = userService.Artist?.ArtistProfile?.ProfilePictureUrl ?? "/images/default-profile.png",
                CategoryName = userService.Service?.ServiceCategory?.Name,
                SelectedLocationType = LocationType.WalkIn,
                IsNewClient = isNewClient
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

                // ─── CHECK "I AGREE" ACKNOWLEDGMENT ───
                if (!model.HasAgreedToTerms)
                {
                    ModelState.AddModelError("HasAgreedToTerms", "You must agree to the terms before booking.");
                    return View("BookService", model);
                }

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
                        model.CardProcessingFee = CalculateCardProcessingFee(userService.Price);
                        model.ClientTotal = CalculateClientTotal(userService.Price);
                        model.ArtistId = userService.ArtistId;
                        model.ArtistName = !string.IsNullOrEmpty(userService.Artist?.FirstName)
                            ? $"{userService.Artist.FirstName} {userService.Artist.LastName}".Trim()
                            : userService.Artist?.UserName ?? "Pro Artist";
                        model.ArtistProfilePicture = userService.Artist?.ArtistProfile?.ProfilePictureUrl ?? "/images/default-profile.png";
                        model.CategoryName = userService.Service?.ServiceCategory?.Name;
                        model.IsNewClient = await IsNewClient(currentUser.Id, userService.ArtistId);
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
                        model.CardProcessingFee = CalculateCardProcessingFee(userService.Price);
                        model.ClientTotal = CalculateClientTotal(userService.Price);
                        model.ArtistId = userService.ArtistId;
                        model.ArtistName = !string.IsNullOrEmpty(userService.Artist?.FirstName)
                            ? $"{userService.Artist.FirstName} {userService.Artist.LastName}".Trim()
                            : userService.Artist?.UserName ?? "Pro Artist";
                        model.ArtistProfilePicture = userService.Artist?.ArtistProfile?.ProfilePictureUrl ?? "/images/default-profile.png";
                        model.CategoryName = userService.Service?.ServiceCategory?.Name;
                        model.IsNewClient = await IsNewClient(currentUser.Id, userService.ArtistId);
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

                // ─── CALCULATE FEES ───
                decimal servicePrice = model.Price;
                bool isNewClient = await IsNewClient(currentUser.Id, model.ArtistId);
                decimal cardProcessingFee = CalculateCardProcessingFee(servicePrice);
                decimal clientTotal = CalculateClientTotal(servicePrice);
                decimal platformFee = isNewClient
                    ? servicePrice * NEW_CLIENT_COMMISSION
                    : REPEAT_CLIENT_FLAT_FEE;
                platformFee = Math.Max(platformFee, MIN_PLATFORM_FEE);
                decimal artistPayout = servicePrice - platformFee;

                // ─── DEPOSIT & FINAL ───
                decimal depositAmount = CalculateDepositAmount(servicePrice);
                decimal finalAmount = CalculateFinalAmount(servicePrice);

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
                    CardProcessingFee = cardProcessingFee,
                    BookingFee = BOOKING_FEE,

                    PlatformCommission = platformFee,
                    PlatformEarnings = cardProcessingFee + BOOKING_FEE + platformFee,
                    ArtistNetAmount = artistPayout,
                    ArtistTotalEarned = 0m,

                    DepositAmount = depositAmount,
                    FinalAmount = finalAmount,
                    DepositPaid = 0m,
                    FinalPaymentPaid = 0m,
                    IsDepositPaid = false,

                    // ─── CANCELLATION/REFUND TRACKING ───
                    RefundAmount = 0m,
                    RefundDate = null,
                    IsRefunded = false,

                    // ─── LIFECYCLE PROPERTIES ───
                    ConfirmationPromptSentAt = null,
                    AutoConfirmAt = null,
                    IsDisputed = false,
                    DisputeReason = null,
                    DisputeDescription = null,
                    DisputeRaisedAt = null,
                    AdminReviewedAt = null,
                    AdminResolution = null,
                    AdminResolutionAmount = 0m,
                    CompletedAt = null,
                    FundsReleasedAt = null,
                    IsFundsReleased = false,
                    IsCompleted = false,

                    AvailabilitySlotId = slot.Id,

                    HouseNumber = model.HouseNumber ?? "",
                    StreetAddress = model.StreetAddress ?? "",
                    AreaCode = model.AreaCode ?? "",
                    HouseCallAddress = fullAddress,
                    Latitude = model.Latitude ?? "",
                    Longitude = model.Longitude ?? "",
                    IsNewClient = isNewClient
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

            var depositAmount = isLastMinute
                ? booking.TotalAmount
                : booking.DepositAmount;

            var model = new CheckoutViewModel
            {
                Booking = booking,
                DepositAmount = depositAmount,
                UserEmail = currentUser.Email,
                UserName = $"{currentUser.FirstName} {currentUser.LastName}",
                IsLastMinute = isLastMinute,
                IsNewClient = booking.IsNewClient,
                ArtistPayout = booking.ArtistNetAmount,
                PlatformFee = booking.PlatformCommission,
                ClientMarkup = booking.CardProcessingFee,
                BookingFee = booking.BookingFee,
                ServicePrice = booking.ServicePrice,
                ClientTotal = booking.TotalAmount,
                CardProcessingFee = booking.CardProcessingFee
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

                var depositAmount = booking.DepositAmount;

                return RedirectToAction("InitiatePayment", "Payment", new
                {
                    bookingId = id,
                    email = currentUser.Email,
                    amount = depositAmount
                });
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

                var remainingBalance = booking.FinalAmount - booking.FinalPaymentPaid;

                if (remainingBalance <= 0)
                {
                    TempData["Error"] = "This booking has already been fully paid.";
                    return RedirectToAction("MyBookings");
                }

                return RedirectToAction("InitiateFinalPayment", "Payment", new
                {
                    bookingId = id,
                    email = currentUser.Email,
                    amount = remainingBalance
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ProcessFinalPayment error: {ex.Message}");
                TempData["Error"] = "An error occurred. Please try again.";
                return RedirectToAction("MyBookings");
            }
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

                if (!booking.IsDepositPaid)
                {
                    TempData["Error"] = "Please pay the deposit first.";
                    return RedirectToAction("MyBookings");
                }

                var remainingBalance = booking.FinalAmount - booking.FinalPaymentPaid;

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
                    UserName = $"{currentUser.FirstName} {currentUser.LastName}",
                    IsNewClient = booking.IsNewClient,
                    ArtistPayout = booking.ArtistNetAmount,
                    PlatformFee = booking.PlatformCommission,
                    ClientMarkup = booking.CardProcessingFee,
                    BookingFee = booking.BookingFee,
                    ServicePrice = booking.ServicePrice,
                    ClientTotal = booking.TotalAmount,
                    CardProcessingFee = booking.CardProcessingFee
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

            var query = _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                        .ThenInclude(a => a.ArtistProfile)
                .Where(b => b.CustomerId == currentUser.Id && b.UserService != null)
                .AsNoTracking();

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

            if (startDate.HasValue)
                query = query.Where(b => b.AppointmentDate >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(b => b.AppointmentDate <= endDate.Value);

            if (!string.IsNullOrEmpty(status) &&
                Enum.TryParse<BookingStatus>(status, true, out var statusEnum))
            {
                query = query.Where(b => b.Status == statusEnum);
            }

            var totalCount = await query.CountAsync();

            var bookings = await query
                .OrderByDescending(b => b.BookingDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

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

            ViewBag.SelectedMonth = month;
            ViewBag.StartDate = startDate;
            ViewBag.EndDate = endDate;
            ViewBag.SelectedStatus = status;

            return View(model);
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

            // ─── 1. UPDATE STATUS ───
            booking.Status = BookingStatus.Cancelled;
            booking.ClientNotes = clientNotes;

            // ─── 2. RELEASE THE SLOT ───
            if (booking.AvailabilitySlotId.HasValue)
            {
                var slot = await _context.ArtistAvailabilities
                    .FirstOrDefaultAsync(a => a.Id == booking.AvailabilitySlotId.Value);
                if (slot != null) slot.IsBooked = false;
            }

            // ─── 3. NO REFUNDS! ───
            booking.RefundAmount = 0m;
            booking.RefundDate = null;
            booking.IsRefunded = false;

            await _context.SaveChangesAsync();

            // ─── 4. SEND NOTIFICATIONS ───
            try
            {
                if (booking.UserService != null && !string.IsNullOrEmpty(booking.UserService.ArtistId))
                {
                    await _commService.SendDirectMessageEmailAsync(
                        currentUser.Id,
                        booking.UserService.ArtistId,
                        "Booking Cancelled By Client",
                        $"Client {currentUser.FirstName} has cancelled Booking #{booking.Id} for {booking.UserService?.Service?.Name} on {booking.AppointmentDate:MMM dd}. All payments are non-refundable."
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
                    $"You have cancelled your appointment for {booking.UserService?.Service?.Name}. Please note: all payments are non-refundable.",
                    "booking_cancelled",
                    booking.Id.ToString(),
                    Url.Action("MyBookings", "Booking")
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Notification/Email failed for cancellation of booking {booking.Id}: {ex.Message}");
            }

            TempData["Success"] = "Your booking has been cancelled. Please note: all payments are non-refundable.";
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
        //  POST: Booking/CancelByArtist
        // ══════════════════════════════════
        [Authorize(Roles = "Artist")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelByArtist(int id, string? artistNotes)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return Challenge();

            var booking = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.Customer)
                .FirstOrDefaultAsync(b => b.Id == id && b.UserService.ArtistId == currentUser.Id);

            if (booking == null ||
                booking.Status == BookingStatus.Completed ||
                booking.Status == BookingStatus.Cancelled)
                return NotFound();

            // ─── 1. UPDATE STATUS ───
            booking.Status = BookingStatus.Cancelled;
            booking.ArtistNotes = artistNotes ?? "Booking cancelled by artist.";

            // ─── 2. RELEASE THE SLOT ───
            if (booking.AvailabilitySlotId.HasValue)
            {
                var slot = await _context.ArtistAvailabilities
                    .FirstOrDefaultAsync(a => a.Id == booking.AvailabilitySlotId.Value);
                if (slot != null) slot.IsBooked = false;
            }

            // ─── 3. FULL REFUND (ARTIST CANCELLED) ───
            decimal totalPaid = booking.DepositPaid + booking.FinalPaymentPaid;

            if (totalPaid > 0)
            {
                booking.RefundAmount = totalPaid;
                booking.RefundDate = DateTime.UtcNow;
                booking.IsRefunded = true;
                booking.DepositPaid = 0m;
                booking.FinalPaymentPaid = 0m;
                booking.IsDepositPaid = false;

                await _context.SaveChangesAsync();

                try
                {
                    await _notificationService.CreateNotificationAsync(
                        booking.CustomerId,
                        "Booking Cancelled - Refund Processed 💰",
                        $"The artist has cancelled your booking #{booking.Id}. A full refund of R{totalPaid:N2} has been processed.",
                        "booking_cancelled_refund",
                        booking.Id.ToString(),
                        Url.Action("MyBookings", "Booking")
                    );

                    if (booking.Customer != null && !string.IsNullOrEmpty(booking.Customer.Email))
                    {
                        string subject = "💰 Booking Cancelled - Refund Processed";
                        string body = $@"
                        <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #28a745; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
                            <h2 style='color: #28a745;'>💰 Refund Processed</h2>
                            <p>Dear {booking.Customer.FirstName},</p>
                            <p>The artist has cancelled your booking <strong>#{booking.Id}</strong>.</p>
                            <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
                                <p><strong>Service:</strong> {booking.UserService?.Service?.Name ?? "your service"}</p>
                                <p><strong>Refund Amount:</strong> R{totalPaid:N2}</p>
                                <p><strong>Reason:</strong> {booking.ArtistNotes}</p>
                            </div>
                            <p>The refund will reflect in your account within 5-7 business days.</p>
                            <p>We apologize for any inconvenience.</p>
                            <hr style='border-color: #333;'>
                            <p style='font-size: 12px; color: #666;'>RubiOr</p>
                        </div>";

                        await _commService.SendDirectMessageEmailAsync(currentUser.Id, booking.CustomerId, subject, body);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Refund notification error: {ex.Message}");
                }

                TempData["Success"] = $"Booking cancelled. Client has been refunded R{totalPaid:N2}.";
            }
            else
            {
                await _context.SaveChangesAsync();
                TempData["Success"] = "Booking cancelled. No payment was made.";
            }

            return RedirectToAction("MyAppointments", "Artist");
        }

        // ══════════════════════════════════
        //  CONFIRM COMPLETION (Client)
        // ══════════════════════════════════
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> ConfirmCompletion(int id)
        {
            var booking = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                .FirstOrDefaultAsync(b => b.Id == id && b.CustomerId == _userManager.GetUserId(User));

            if (booking == null) return NotFound();

            if (booking.Status != BookingStatus.Confirmed)
            {
                TempData["Error"] = "This booking is not in a confirmable state.";
                return RedirectToAction("MyBookings");
            }

            if (booking.ConfirmationPromptSentAt == null)
            {
                TempData["Error"] = "Confirmation has not been requested yet.";
                return RedirectToAction("MyBookings");
            }

            if (booking.IsCompleted || booking.IsDisputed)
            {
                TempData["Error"] = "This booking has already been processed.";
                return RedirectToAction("MyBookings");
            }

            var model = new ConfirmCompletionViewModel
            {
                BookingId = booking.Id,
                ServiceName = booking.UserService?.Service?.Name ?? "Service",
                ArtistName = !string.IsNullOrEmpty(booking.UserService?.Artist?.FirstName)
                    ? $"{booking.UserService.Artist.FirstName} {booking.UserService.Artist.LastName}".Trim()
                    : "Artist",
                AppointmentDate = booking.AppointmentDate
            };

            return View(model);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmCompletion(int id, bool wasCompleted)
        {
            var booking = await _context.Bookings
                .Include(b => b.UserService)
                .Include(b => b.Customer)
                .FirstOrDefaultAsync(b => b.Id == id && b.CustomerId == _userManager.GetUserId(User));

            if (booking == null) return NotFound();

            if (booking.Status != BookingStatus.Confirmed)
            {
                TempData["Error"] = "This booking is not in a confirmable state.";
                return RedirectToAction("MyBookings");
            }

            if (booking.IsCompleted || booking.IsDisputed)
            {
                TempData["Error"] = "This booking has already been processed.";
                return RedirectToAction("MyBookings");
            }

            if (wasCompleted)
            {
                booking.IsCompleted = true;
                booking.CompletedAt = DateTime.UtcNow;
                booking.Status = BookingStatus.Completed;
                booking.FundsReleasedAt = DateTime.UtcNow;
                booking.IsFundsReleased = true;

                await _context.SaveChangesAsync();

                await ReleaseFundsToArtist(booking);

                // ─── SEND NOTIFICATION ───
                await _notificationService.CreateNotificationAsync(
                    booking.CustomerId,
                    "Service Confirmed",
                    $"You confirmed that your {booking.UserService?.Service?.Name} appointment was completed.",
                    "booking_completed",
                    booking.Id.ToString(),
                    Url.Action("MyBookings", "Booking")
                );

                await _notificationService.CreateNotificationAsync(
                    booking.UserService.ArtistId,
                    "Service Completed",
                    $"{booking.Customer?.FirstName} confirmed that the {booking.UserService?.Service?.Name} service was completed. Funds have been released.",
                    "booking_completed",
                    booking.Id.ToString(),
                    Url.Action("MyAppointments", "Artist")
                );

                // ─── ✅ SEND EMAIL TO CLIENT ───
                if (booking.Customer != null && !string.IsNullOrEmpty(booking.Customer.Email))
                {
                    string clientSubject = "Service Confirmed";
                    string clientBody = $@"
            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #28a745; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
                <h2 style='color: #28a745;'>Service Confirmed</h2>
                <p>Dear {booking.Customer.FirstName},</p>
                <p>You confirmed that <strong>{booking.UserService?.Service?.Name}</strong> was completed.</p>
                <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
                    <p><strong>Service:</strong> {booking.UserService?.Service?.Name}</p>
                    <p><strong>Artist:</strong> {booking.UserService?.Artist?.FirstName}</p>
                    <p><strong>Date:</strong> {booking.AppointmentDate:dddd, MMMM dd, yyyy}</p>
                    <p><strong>Time:</strong> {booking.AppointmentDate:hh:mm tt}</p>
                </div>
                <p>Thank you for choosing RubiOr!</p>
                <hr style='border-color: #333;'>
                <p style='font-size: 12px; color: #666;'>RubiOr</p>
            </div>";

                    await _commService.SendDirectMessageEmailAsync(booking.UserService.ArtistId, booking.CustomerId, clientSubject, clientBody);
                }

                // ─── ✅ SEND EMAIL TO ARTIST ───
                if (booking.UserService?.Artist != null && !string.IsNullOrEmpty(booking.UserService.Artist.Email))
                {
                    string artistSubject = "Service Completed - Funds Released";
                    string artistBody = $@"
            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #f0c808; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
                <h2 style='color: #f0c808;'>Service Completed</h2>
                <p>Dear {booking.UserService.Artist.FirstName},</p>
                <p>{booking.Customer?.FirstName} confirmed that <strong>{booking.UserService?.Service?.Name}</strong> was completed.</p>
                <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
                    <p><strong>Service:</strong> {booking.UserService?.Service?.Name}</p>
                    <p><strong>Client:</strong> {booking.Customer?.FirstName}</p>
                    <p><strong>Date:</strong> {booking.AppointmentDate:dddd, MMMM dd, yyyy}</p>
                    <p><strong>Time:</strong> {booking.AppointmentDate:hh:mm tt}</p>
                    <p><strong>Amount Released:</strong> R{(booking.DepositPaid + booking.FinalPaymentPaid):N2}</p>
                </div>
                <p>Funds have been released to your account.</p>
                <hr style='border-color: #333;'>
                <p style='font-size: 12px; color: #666;'>RubiOr</p>
            </div>";

                    await _commService.SendDirectMessageEmailAsync(booking.CustomerId, booking.UserService.ArtistId, artistSubject, artistBody);
                }

                TempData["Success"] = "Thank you for confirming! The artist has been paid.";
                return RedirectToAction("MyBookings");
            }
            else
            {
                return RedirectToAction("DisputeBooking", new { id = booking.Id });
            }
        }
        // ══════════════════════════════════
        //  DISPUTE BOOKING (Client)
        // ══════════════════════════════════
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> DisputeBooking(int id)
        {
            var booking = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .FirstOrDefaultAsync(b => b.Id == id && b.CustomerId == _userManager.GetUserId(User));

            if (booking == null) return NotFound();

            if (booking.IsDisputed)
            {
                TempData["Error"] = "This booking is already under dispute.";
                return RedirectToAction("MyBookings");
            }

            if (booking.Status != BookingStatus.Confirmed)
            {
                TempData["Error"] = "This booking cannot be disputed at this stage.";
                return RedirectToAction("MyBookings");
            }

            var model = new DisputeViewModel
            {
                BookingId = booking.Id,
                ServiceName = booking.UserService?.Service?.Name ?? "Service"
            };

            return View(model);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DisputeBooking(DisputeViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var booking = await _context.Bookings
                .Include(b => b.UserService)
                .Include(b => b.Customer)
                .FirstOrDefaultAsync(b => b.Id == model.BookingId && b.CustomerId == _userManager.GetUserId(User));

            if (booking == null) return NotFound();

            if (booking.IsDisputed)
            {
                TempData["Error"] = "This booking is already under dispute.";
                return RedirectToAction("MyBookings");
            }

            booking.IsDisputed = true;
            booking.DisputeReason = model.Reason;
            booking.DisputeDescription = model.Description;
            booking.DisputeRaisedAt = DateTime.UtcNow;
            booking.Status = BookingStatus.Disputed;

            await _context.SaveChangesAsync();

            // ─── SEND NOTIFICATIONS ───
            var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");
            foreach (var admin in adminUsers)
            {
                await _notificationService.CreateNotificationAsync(
                    admin.Id,
                    "New Booking Dispute",
                    $"Client has disputed booking #{booking.Id} for {booking.UserService?.Service?.Name}. Reason: {model.Reason}",
                    "dispute_raised",
                    booking.Id.ToString(),
                    Url.Action("DisputeDetail", "Admin", new { id = booking.Id })
                );
            }

            await _notificationService.CreateNotificationAsync(
                booking.CustomerId,
                "Dispute Submitted",
                $"Your dispute for {booking.UserService?.Service?.Name} has been submitted. An admin will review it within 24 hours.",
                "dispute_submitted",
                booking.Id.ToString(),
                Url.Action("MyBookings", "Booking")
            );

            await _notificationService.CreateNotificationAsync(
                booking.UserService.ArtistId,
                "Booking Disputed",
                $"Client has disputed the {booking.UserService?.Service?.Name} appointment. An admin will review the case.",
                "dispute_raised_artist",
                booking.Id.ToString(),
                Url.Action("MyAppointments", "Artist")
            );

            // ─── ✅ SEND EMAIL TO ADMIN ───
            foreach (var admin in adminUsers)
            {
                if (!string.IsNullOrEmpty(admin.Email))
                {
                    string adminSubject = "New Booking Dispute";
                    string adminBody = $@"
            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #e50914; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
                <h2 style='color: #e50914;'>New Booking Dispute</h2>
                <p>Dear Admin,</p>
                <p>A client has disputed booking <strong>#{booking.Id}</strong>.</p>
                <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
                    <p><strong>Booking ID:</strong> #{booking.Id}</p>
                    <p><strong>Client:</strong> {booking.Customer?.FirstName} {booking.Customer?.LastName}</p>
                    <p><strong>Service:</strong> {booking.UserService?.Service?.Name}</p>
                    <p><strong>Reason:</strong> {model.Reason}</p>
                    <p><strong>Description:</strong> {model.Description}</p>
                    <p><strong>Amount:</strong> R{(booking.DepositPaid + booking.FinalPaymentPaid):N2}</p>
                </div>
                <div style='text-align: center; margin: 20px 0;'>
                    <a href='{Url.Action("DisputeDetail", "Admin", new { id = booking.Id })}' style='background: #e50914; color: #fff; padding: 12px 30px; text-decoration: none; border-radius: 8px; font-weight: 700; display: inline-block;'>
                        Review Dispute
                    </a>
                </div>
                <hr style='border-color: #333;'>
                <p style='font-size: 12px; color: #666;'>RubiOr</p>
            </div>";

                    await _commService.SendDirectMessageEmailAsync(booking.CustomerId, admin.Id, adminSubject, adminBody);
                }
            }

            // ─── ✅ SEND EMAIL TO CLIENT ───
            if (booking.Customer != null && !string.IsNullOrEmpty(booking.Customer.Email))
            {
                string clientSubject = "Dispute Submitted";
                string clientBody = $@"
        <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #f0c808; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
            <h2 style='color: #f0c808;'>Dispute Submitted</h2>
            <p>Dear {booking.Customer.FirstName},</p>
            <p>Your dispute for <strong>{booking.UserService?.Service?.Name}</strong> has been submitted.</p>
            <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
                <p><strong>Service:</strong> {booking.UserService?.Service?.Name}</p>
                <p><strong>Artist:</strong> {booking.UserService?.Artist?.FirstName}</p>
                <p><strong>Reason:</strong> {model.Reason}</p>
            </div>
            <p>An admin will review it within 24 hours.</p>
            <hr style='border-color: #333;'>
            <p style='font-size: 12px; color: #666;'>RubiOr</p>
        </div>";

                await _commService.SendDirectMessageEmailAsync(booking.UserService.ArtistId, booking.CustomerId, clientSubject, clientBody);
            }

            // ─── ✅ SEND EMAIL TO ARTIST ───
            if (booking.UserService?.Artist != null && !string.IsNullOrEmpty(booking.UserService.Artist.Email))
            {
                string artistSubject = "Booking Disputed";
                string artistBody = $@"
        <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #e50914; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
            <h2 style='color: #e50914;'>Booking Disputed</h2>
            <p>Dear {booking.UserService.Artist.FirstName},</p>
            <p>A client has disputed the <strong>{booking.UserService?.Service?.Name}</strong> appointment.</p>
            <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
                <p><strong>Service:</strong> {booking.UserService?.Service?.Name}</p>
                <p><strong>Client:</strong> {booking.Customer?.FirstName}</p>
                <p><strong>Reason:</strong> {model.Reason}</p>
            </div>
            <p>An admin will review the case within 24 hours.</p>
            <hr style='border-color: #333;'>
            <p style='font-size: 12px; color: #666;'>RubiOr</p>
        </div>";

                await _commService.SendDirectMessageEmailAsync(booking.CustomerId, booking.UserService.ArtistId, artistSubject, artistBody);
            }

            TempData["Success"] = "Your dispute has been raised. An admin will review it within 24 hours.";
            return RedirectToAction("MyBookings");
        }

        // ─── HELPER: Release funds to artist ───
        private async Task ReleaseFundsToArtist(Booking booking)
        {
            try
            {
                var artistProfile = await _context.ArtistProfiles
                    .FirstOrDefaultAsync(p => p.UserId == booking.UserService.ArtistId);

                if (artistProfile == null || string.IsNullOrEmpty(artistProfile.SubaccountCode))
                {
                    Console.WriteLine($"⚠️ No subaccount for artist {booking.UserService.ArtistId}");
                    return;
                }

                decimal totalPaid = booking.DepositPaid + booking.FinalPaymentPaid;

                if (totalPaid <= 0)
                {
                    Console.WriteLine($"⚠️ No payment found for booking {booking.Id}");
                    return;
                }

                booking.ArtistTotalEarned = totalPaid;

                await _context.SaveChangesAsync();

                Console.WriteLine($"✅ Released R{totalPaid} to artist {booking.UserService.ArtistId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ReleaseFundsToArtist error: {ex.Message}");
            }
        }
    }
}