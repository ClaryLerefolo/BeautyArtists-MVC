using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;

namespace BeautyArtists.Controllers
{
    public class BookingController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public BookingController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Book()
        {
            var services = await _context.UserServices
                .Include(us => us.Service)
                .Include(us => us.Artist)
                    .ThenInclude(a => a.ArtistProfile)
                .ToListAsync();

            return View(services);
        }


        // GET: Booking/BookService
        [Authorize]
        public async Task<IActionResult> BookService(int userServiceId, string bookingType)
        {
            var userService = await _context.UserServices
                .Include(us => us.Service)
                    .ThenInclude(s => s.ServiceCategory)
                .Include(us => us.Artist)
                    .ThenInclude(a => a.ArtistProfile)
                .FirstOrDefaultAsync(us => us.Id == userServiceId);

            if (userService == null) return NotFound();

            var artistName = !string.IsNullOrEmpty(userService.Artist?.FirstName)
                ? $"{userService.Artist.FirstName} {userService.Artist.LastName}".Trim()
                : userService.Artist?.UserName ?? "Pro Artist";

            // Parse the incoming string bookingType parameter ("WalkIn" or "HouseCall") 
            // into your existing LocationType enum. If missing or invalid, default to WalkIn.
            LocationType selectedLocation = LocationType.WalkIn;
            if (!string.IsNullOrEmpty(bookingType) && Enum.TryParse(bookingType, true, out LocationType parsedType))
            {
                selectedLocation = parsedType;
            }

            var model = new BookingViewModel
            {
                UserServiceId = userServiceId,
                ServiceName = userService.Service?.Name,
                Price = userService.Price,
                ArtistName = artistName,
                ArtistId = userService.ArtistId,  // ← CRITICAL for slot fetching
                ArtistProfilePicture = userService.Artist?.ArtistProfile?.ProfilePictureUrl
                                       ?? "/images/default-profile.png",
                CategoryName = userService.Service?.ServiceCategory?.Name,

                // Pre-populate the user's selection from the catalogue choice here:
                SelectedLocationType = selectedLocation
            };

            return View("BookService", model);
        }

        // POST: Booking/ConfirmBooking
        [Authorize]
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> ConfirmBooking(BookingViewModel model)
{
    if (!User.Identity.IsAuthenticated)
        return Challenge();

    var currentUser = await _userManager.GetUserAsync(User);

    // Custom Validation: If House Call is chosen, ensure location data exists
    if (model.SelectedLocationType == LocationType.HouseCall)
    {
        if (string.IsNullOrWhiteSpace(model.HouseCallAddress))
        {
            ModelState.AddModelError("HouseCallAddress", "An address is required for house calls.");
        }
        if (!model.Latitude.HasValue || !model.Longitude.HasValue)
        {
            ModelState.AddModelError("", "Please pin your exact location on the map map.");
        }
    }

    if (!ModelState.IsValid)
    {
        // Reload service details if page validation failed
        var userService = await _context.UserServices
            .Include(us => us.Service)
            .Include(us => us.Artist)
                .ThenInclude(a => a.ArtistProfile)
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
        }
        return View("BookService", model);
    }

    // 1. Verify availability slot
    var slot = await _context.ArtistAvailabilities
        .FirstOrDefaultAsync(a => a.Id == model.AvailabilitySlotId && !a.IsBooked);

    if (slot == null)
    {
        ModelState.AddModelError("", "Sorry, this slot was just booked by someone else. Please select another.");
        return View("BookService", model);
    }

    model.PreferredDate = slot.AvailableDate.Add(slot.StartTime);

    // 2. Map view data right down to database entity object
    var booking = new Booking
    {
        CustomerId = currentUser.Id,
        UserServiceId = model.UserServiceId,
        BookingDate = DateTime.UtcNow,
        AppointmentDate = model.PreferredDate,
        Notes = model.Notes,
        HasRescheduled = false,
        Status = Booking.BookingStatus.Pending,
        
        // Save the flow selections
        SelectLocationType = model.SelectedLocationType,
        TransportCost = 0, // Remains zero; changed later by Artist if HouseCall

        // TotalAmount stays at base price for now. 
        // If it's a house call, total increments once the artist reviews and adds travel costs.
        TotalAmount = model.Price 
    };

    // If it's a house call, append map point properties 
    if (model.SelectedLocationType == LocationType.HouseCall)
    {
        booking.HouseCallAddress = model.HouseCallAddress;
        booking.Latitude = model.Latitude;
        booking.Longitude = model.Longitude;
    }

    // 3. Flag slot to avoid double booking conflicts
    slot.IsBooked = true;

    _context.Bookings.Add(booking);
    await _context.SaveChangesAsync();

    if (booking.SelectLocationType == LocationType.HouseCall)
    {
        TempData["Success"] = "House Call request sent! Awaiting artist to calculate transport cost and accept.";
    }
    else
    {
        TempData["Success"] = "Walk-In booking submitted! Pending artist confirmation.";
    }

    return RedirectToAction("MyBookings");
}

        public async Task<IActionResult> MyBookings()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return Challenge(); // force login

            var bookings = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService.Artist)
                    .ThenInclude(a => a.ArtistProfile)
                .Where(b => b.CustomerId == currentUser.Id)
                .OrderByDescending(b => b.BookingDate)
                .ToListAsync();

            return View(bookings); // View: Views/Booking/MyBookings.cshtml
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id, string? clientNotes)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var booking = await _context.Bookings
                .FirstOrDefaultAsync(b => b.Id == id && b.CustomerId == currentUser.Id);

            if (booking == null || booking.Status == Booking.BookingStatus.Completed
                || booking.Status == Booking.BookingStatus.Cancelled)
                return NotFound();

            booking.Status = Booking.BookingStatus.Cancelled;
            booking.ClientNotes = clientNotes; // ← save the reason

            await _context.SaveChangesAsync();
            TempData["Success"] = "Your booking has been cancelled.";
            return RedirectToAction("MyBookings");
        }
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

            // Guard: must exist, confirmed, not already rescheduled
            if (booking == null || booking.HasRescheduled || booking.Status != Booking.BookingStatus.Confirmed)
                return NotFound();

            // Guard: 24-hour rule — cannot reschedule within 24hrs of appointment
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
                ArtistId = booking.UserService?.ArtistId,  // needed to fetch slots
                Price = booking.TotalAmount
            };

            return View("Reschedule", model);
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reschedule(BookingViewModel model)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var booking = await _context.Bookings
                .Include(b => b.UserService)
                .FirstOrDefaultAsync(b => b.Id == model.BookingId && b.CustomerId == currentUser.Id);

            if (booking == null || booking.Status != Booking.BookingStatus.Confirmed || booking.HasRescheduled)
                return NotFound();

            if (booking.AppointmentDate <= DateTime.Now.AddHours(24))
            {
                TempData["Error"] = "Rescheduling is only allowed at least 24 hours before your appointment.";
                return RedirectToAction("MyBookings");
            }

            var newSlot = await _context.ArtistAvailabilities
                .FirstOrDefaultAsync(a => a.Id == model.AvailabilitySlotId
                                       && a.ArtistId == booking.UserService.ArtistId
                                       && !a.IsBooked);

            if (newSlot == null)
            {
                TempData["Error"] = "That slot is no longer available. Please choose another.";
                return RedirectToAction("Reschedule", new { id = model.BookingId });
            }

            var oldSlot = await _context.ArtistAvailabilities
                .FirstOrDefaultAsync(a => a.ArtistId == booking.UserService.ArtistId
                                       && a.AvailableDate.Date == booking.AppointmentDate.Date
                                       && booking.AppointmentDate.TimeOfDay >= a.StartTime
                                       && booking.AppointmentDate.TimeOfDay < a.EndTime);
            if (oldSlot != null) oldSlot.IsBooked = false;

            booking.AppointmentDate = newSlot.AvailableDate.Add(newSlot.StartTime);
            booking.HasRescheduled = true;
            booking.ClientNotes = model.Notes; // ← reschedule reason goes into ClientNotes
            newSlot.IsBooked = true;

            await _context.SaveChangesAsync();
            TempData["Success"] = $"Rescheduled to {newSlot.AvailableDate:MMM dd} at {newSlot.StartTime:hh\\:mm}!";
            return RedirectToAction("MyBookings");
        }



    }

}

