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

        public async Task<IActionResult> BookService(int userServiceId)
        {
            var userService = await _context.UserServices
                .Include(us => us.Service)
                .Include(us => us.Artist)
                .FirstOrDefaultAsync(us => us.Id == userServiceId);

            if (userService == null) return NotFound();

            var model = new BookingViewModel
            {
                UserServiceId = userServiceId,
                ServiceName = userService.Service?.Name,
                Price = userService.Price,
                ArtistName = userService.Artist.ArtistProfile?.FullName ?? $"{userService.Artist.FirstName} {userService.Artist.LastName}"
            };

            return View("BookService", model); // View = Views/Booking/BookService.cshtml
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmBooking(BookingViewModel model)
        {
            if (!ModelState.IsValid)
            {
                foreach (var error in ModelState.Values.SelectMany(v => v.Errors))
                {
                    Console.WriteLine("Validation error: " + error.ErrorMessage); // For development only
                }

                var userService = await _context.UserServices
                    .Include(us => us.Service)
                    .Include(us => us.Artist)
                    .FirstOrDefaultAsync(us => us.Id == model.UserServiceId);

                if (userService == null) return NotFound();

                model.ServiceName = userService.Service?.Name ?? "No Name";
                model.Price = userService.Price;
                model.ArtistName = userService.Artist.ArtistProfile?.FullName ?? $"{userService.Artist.FirstName} {userService.Artist.LastName}";

                return View("BookService", model);
            }

            var currentUser = await _userManager.GetUserAsync(User);
            if (!User.Identity.IsAuthenticated)
            {
                return Challenge(); // Will redirect to the default Identity login page
            }
            var booking = new Booking
            {
                CustomerId = currentUser.Id,
                UserServiceId = model.UserServiceId,
                BookingDate = DateTime.UtcNow,
                AppointmentDate = model.PreferredDate,
                TotalAmount = model.Price,
                Status = Booking.BookingStatus.Pending,
                Notes = model.Notes,
                HasRescheduled = false
            };

            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Your booking request has been submitted and is pending approval.";
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
        public async Task<IActionResult> Cancel(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var booking = await _context.Bookings
                .FirstOrDefaultAsync(b => b.Id == id && b.CustomerId == currentUser.Id);

            if (booking == null || booking.Status == Booking.BookingStatus.Completed || booking.Status == Booking.BookingStatus.Cancelled)
            {
                return NotFound();
            }

            booking.Status = Booking.BookingStatus.Cancelled;
            await _context.SaveChangesAsync();

            TempData["Success"] = "Your booking has been cancelled.";
            return RedirectToAction("MyBookings");
        }
        [HttpGet]
        public async Task<IActionResult> Reschedule(int id)
        {
            var currentUserId = _userManager.GetUserId(User); // this one actually returns string directly
            var booking = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService.Artist)
                    .ThenInclude(a => a.ArtistProfile)
                .FirstOrDefaultAsync(b => b.Id == id && b.CustomerId == currentUserId);


            if (booking == null || booking.HasRescheduled || booking.Status != Booking.BookingStatus.Confirmed)
                return NotFound();

            var model = new BookingViewModel
            {
                BookingId = booking.Id,
                PreferredDate = booking.AppointmentDate,
                Notes = booking.Notes,
                ServiceName = booking.UserService?.Service?.Name,
                ArtistName = booking.UserService?.Artist?.ArtistProfile?.FullName,
                Price = booking.TotalAmount
            };

            return View("Reschedule", model); // make sure this view exists
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reschedule(BookingViewModel model)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var booking = await _context.Bookings
                .FirstOrDefaultAsync(b => b.Id == model.BookingId && b.CustomerId == currentUser.Id);

            if (booking == null || booking.Status != Booking.BookingStatus.Confirmed || booking.HasRescheduled)
                return NotFound();

            booking.AppointmentDate = model.PreferredDate;
            booking.HasRescheduled = true;

            await _context.SaveChangesAsync();

            TempData["Success"] = "Your booking has been rescheduled.";
            return RedirectToAction("MyBookings");
        }




    }

}

