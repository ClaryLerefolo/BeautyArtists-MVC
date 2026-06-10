using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Models.ViewModels;
using BeautyArtists.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
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

        public BookingController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, ICommunicationService commService)
        {
            _context = context;
            _userManager = userManager;
            _commService = commService;
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

        // ═══════════════════════════════════════════════════════════
        //  GET: Booking/GetArtistAvailability - FIXED FOR CALENDAR
        // ═══════════════════════════════════════════════════════════
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
                .ToListAsync();

            return Json(slots);
        }

        // ═══════════════════════════════════════════════════════════
        //  DEBUG: Check slots in database (REMOVE AFTER TESTING)
        // ═══════════════════════════════════════════════════════════
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

            var model = new BookingViewModel
            {
                UserServiceId = userServiceId,
                ServiceName = userService.Service?.Name,
                Price = userService.Price,
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
            if (!User.Identity.IsAuthenticated)
                return Challenge();

            var currentUser = await _userManager.GetUserAsync(User);

            // ── HOUSE CALL SPECIFIC VALIDATION ──
            if (model.SelectedLocationType == LocationType.HouseCall)
            {
                if (string.IsNullOrWhiteSpace(model.HouseCallAddress))
                {
                    ModelState.AddModelError("HouseCallAddress", "An address is required for house calls.");
                }

                if (string.IsNullOrEmpty(model.Latitude) || string.IsNullOrEmpty(model.Longitude))
                {
                    ModelState.AddModelError(string.Empty, "Please pin your exact location on the map.");
                }
            }

            // ── IF VALIDATION FAILS: REPOPULATE AND RETURN VIEW ──
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
                    model.ArtistId = userService.ArtistId;
                    model.ArtistName = !string.IsNullOrEmpty(userService.Artist?.FirstName)
                        ? $"{userService.Artist.FirstName} {userService.Artist.LastName}".Trim()
                        : userService.Artist?.UserName ?? "Pro Artist";
                    model.ArtistProfilePicture = userService.Artist?.ArtistProfile?.ProfilePictureUrl ?? "/images/default-profile.png";
                    model.CategoryName = userService.Service?.ServiceCategory?.Name;
                }

                return View("BookService", model);
            }

            // ── FETCH & LOCK SLOT ──
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

            // ── CREATE INITIAL BOOKING AS PENDING ──
            var booking = new Booking
            {
                CustomerId = currentUser.Id,
                UserServiceId = model.UserServiceId,
                BookingDate = DateTime.UtcNow,
                AppointmentDate = appointmentDate,
                Notes = model.Notes,
                HasRescheduled = false,
                Status = BookingStatus.Pending,
                SelectedLocationType = model.SelectedLocationType.GetValueOrDefault(LocationType.WalkIn),
                TransportCost = 0,
                TotalAmount = model.Price,
                IsDepositPaid = false,
                AvailabilitySlotId = slot.Id
            };

            if (model.SelectedLocationType == LocationType.HouseCall)
            {
                booking.HouseCallAddress = model.HouseCallAddress;
                booking.Latitude = model.Latitude;
                booking.Longitude = model.Longitude;
            }

            slot.IsBooked = true;
            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync();

            // Notify artist
            if (!string.IsNullOrEmpty(slot.ArtistId))
            {
                await _commService.SendBookingRequestToArtistAsync(slot.ArtistId, booking.Id);
            }

            if (booking.SelectedLocationType == LocationType.WalkIn)
            {
                TempData["Success"] = "Appointment requested successfully! The Artist must review and confirm your slot before deposit payment can be processed.";
            }
            else
            {
                TempData["Success"] = "House Call request sent! The Artist will review your location coordinates, apply any relevant transport costs, and confirm.";
            }

            return RedirectToAction("MyBookings");
        }

        // ══════════════════════════════════
        //  POST: Booking/ArtistUpdateStatus
        // ══════════════════════════════════
        // Update your existing ArtistUpdateStatus method in BookingController
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ArtistUpdateStatus(int bookingId, Booking.BookingStatus newStatus, string artistNotes, decimal transportCost)
        {
            var booking = await _context.Bookings
                .Include(b => b.UserService)
                .FirstOrDefaultAsync(b => b.Id == bookingId);

            if (booking == null) return NotFound();

            booking.Status = newStatus;
            booking.ArtistNotes = artistNotes; // Fixed: should be ArtistNotes, not ClientNotes

            if (newStatus == Booking.BookingStatus.Confirmed)
            {
                if (booking.SelectedLocationType == LocationType.HouseCall)
                {
                    // If transport cost was already set, keep it; otherwise use the one passed
                    if (transportCost > 0 || booking.TransportCost == 0)
                    {
                        booking.TransportCost = transportCost >= 0 ? transportCost : 0;
                    }
                    booking.TotalAmount = (booking.UserService?.Price ?? 0) + booking.TransportCost;
                }
                TempData["Success"] = "Appointment status successfully updated to Confirmed.";

                await _context.SaveChangesAsync();

                // Send deposit payment notification
                var callbackUrl = Url.Action("CheckoutDeposit", "Booking", new { id = booking.Id }, Request.Scheme);
                await _commService.SendDirectMessageEmailAsync(
                    booking.UserService?.ArtistId,
                    booking.CustomerId,
                    "Deposit Payment Required",
                    $"Your booking has been confirmed! Please pay your 50% deposit here: {callbackUrl}"
                );
            }
            else if (newStatus == Booking.BookingStatus.Cancelled || newStatus == Booking.BookingStatus.Rejected)
            {
                if (booking.AvailabilitySlotId.HasValue)
                {
                    var slot = await _context.ArtistAvailabilities.FirstOrDefaultAsync(a => a.Id == booking.AvailabilitySlotId.Value);
                    if (slot != null) slot.IsBooked = false;
                }
                TempData["Success"] = $"Appointment request has been {newStatus.ToString().ToLower()}.";
                await _context.SaveChangesAsync();

                // Notify client of rejection/cancellation
                await _commService.SendBookingStatusUpdateAsync(booking.CustomerId, booking.Id, newStatus.ToString());
            }
            else if (newStatus == Booking.BookingStatus.Completed)
            {
                TempData["Success"] = "Booking marked as Completed.";
                await _context.SaveChangesAsync();
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

            var booking = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .FirstOrDefaultAsync(b => b.Id == id && b.CustomerId == currentUser.Id);

            if (booking == null) return NotFound();

            if (booking.Status == BookingStatus.Pending)
            {
                TempData["Error"] = "This booking is currently pending artist approval. You can pay your 50% deposit once confirmed.";
                return RedirectToAction("MyBookings");
            }

            if (booking.IsDepositPaid || booking.Status == BookingStatus.Cancelled)
            {
                TempData["Error"] = "This booking is either already paid or has been cancelled.";
                return RedirectToAction("MyBookings");
            }

            return View(booking);
        }

        // ══════════════════════════════════
        //  POST: Booking/ProcessDepositPayment
        // ══════════════════════════════════
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessDepositPayment(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);

            var booking = await _context.Bookings
                .Include(b => b.UserService)
                .FirstOrDefaultAsync(b => b.Id == id && b.CustomerId == currentUser.Id);

            if (booking == null) return NotFound();

            if (booking.Status == BookingStatus.Pending)
            {
                TempData["Error"] = "Action Denied: You cannot process payments for an unconfirmed appointment.";
                return RedirectToAction("MyBookings");
            }

            booking.IsDepositPaid = true;
            await _context.SaveChangesAsync();

            if (booking.UserService != null && !string.IsNullOrEmpty(booking.UserService.ArtistId))
            {
                await _commService.SendDirectMessageEmailAsync(
                    currentUser.Id,
                    booking.UserService.ArtistId,
                    "Deposit Paid - Schedule Secured",
                    $"Client {currentUser.FirstName} has successfully settled the 50% deposit for Booking #{booking.Id}."
                );
            }

            TempData["Success"] = "First 50% Deposit cleared! Your appointment is locked in.";
            return RedirectToAction("MyBookings");
        }

        // ══════════════════════════════════
        //  POST: Booking/ProcessFinalPayment
        // ══════════════════════════════════
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessFinalPayment(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);

            var booking = await _context.Bookings
                .FirstOrDefaultAsync(b => b.Id == id && b.CustomerId == currentUser.Id);

            if (booking == null) return NotFound();

            if (!booking.IsDepositPaid)
            {
                TempData["Error"] = "You must pay the initial 50% deposit before fulfilling the final settlement balance.";
                return RedirectToAction("MyBookings");
            }

            if (booking.TotalAmount == 0)
            {
                TempData["Error"] = "This ledger execution balance cycle has already been fully finalized.";
                return RedirectToAction("MyBookings");
            }

            double daysUntilAppointment = (booking.AppointmentDate.Date - DateTime.Now.Date).TotalDays;
            if (daysUntilAppointment < 2)
            {
                TempData["Error"] = "Payment Processing Lockout: Final settlements must be cleared at least 2 days before the scheduled execution date.";
                return RedirectToAction("MyBookings");
            }

            booking.TotalAmount = 0;
            await _context.SaveChangesAsync();

            TempData["Success"] = "Final settlement cleared! See you at your session.";
            return RedirectToAction("MyBookings");
        }

        // ══════════════════════════════════
        //  GET: Booking/MyBookings
        // ══════════════════════════════════
        public async Task<IActionResult> MyBookings()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return Challenge();

            var bookings = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService.Artist)
                    .ThenInclude(a => a.ArtistProfile)
                .Where(b => b.CustomerId == currentUser.Id)
                .OrderByDescending(b => b.BookingDate)
                .AsNoTracking()
                .ToListAsync();

            return View(bookings);
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

            var booking = await _context.Bookings
                .Include(b => b.UserService)
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

            if (booking.UserService != null && !string.IsNullOrEmpty(booking.UserService.ArtistId))
            {
                await _commService.SendDirectMessageEmailAsync(
                    currentUser.Id,
                    booking.UserService.ArtistId,
                    "Booking Cancelled By Client",
                    $"Client {currentUser.FirstName} has cancelled Booking #{booking.Id}."
                );
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

            var booking = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                        .ThenInclude(s => s.ServiceCategory)
                .Include(b => b.UserService.Artist)
                    .ThenInclude(a => a.ArtistProfile)
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
                Price = booking.TotalAmount
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
    }
}