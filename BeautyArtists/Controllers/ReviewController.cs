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
    [Authorize]
    public class ReviewController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly INotificationService _notificationService;

        public ReviewController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            INotificationService notificationService)
        {
            _context = context;
            _userManager = userManager;
            _notificationService = notificationService;
        }

        // GET: Review/Create/5
        [HttpGet]
        public async Task<IActionResult> Create(int bookingId)
        {
            var currentUser = await _userManager.GetUserAsync(User);

            var booking = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService.Artist)
                .FirstOrDefaultAsync(b => b.Id == bookingId && b.CustomerId == currentUser.Id);

            if (booking == null)
                return NotFound();

            // Check if booking is completed
            if (booking.Status != BookingStatus.Completed)
            {
                TempData["Error"] = "You can only review completed services.";
                return RedirectToAction("MyBookings", "Booking");
            }

            // Check if already reviewed
            var existingReview = await _context.Reviews
                .FirstOrDefaultAsync(r => r.BookingId == bookingId);

            if (existingReview != null)
            {
                TempData["Error"] = "You have already reviewed this service.";
                return RedirectToAction("MyBookings", "Booking");
            }

            var model = new ReviewViewModel
            {
                BookingId = booking.Id,
                ServiceName = booking.UserService?.Service?.Name ?? "Service",
                ArtistName = booking.UserService?.Artist?.FirstName ?? "Artist",
                AppointmentDate = booking.AppointmentDate
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ReviewViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                return Challenge(); // Should never happen with [Authorize]
            }

            // 🔹 FIX: Include UserService AND its Service + Artist
            var booking = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                .FirstOrDefaultAsync(b => b.Id == model.BookingId && b.CustomerId == currentUser.Id);

            if (booking == null)
                return NotFound();

            // 🔹 FIX: Explicitly validate that UserService exists
            if (booking.UserService == null)
            {
                TempData["Error"] = "This booking has missing service details. Please contact support.";
                return RedirectToAction("MyBookings", "Booking");
            }

            // Check if already reviewed
            var existingReview = await _context.Reviews
                .FirstOrDefaultAsync(r => r.BookingId == model.BookingId);

            if (existingReview != null)
            {
                TempData["Error"] = "You have already reviewed this service.";
                return RedirectToAction("MyBookings", "Booking");
            }

            var review = new Review
            {
                Rating = model.Rating,
                Comment = model.Comment,
                CustomerId = currentUser.Id,
                BookingId = model.BookingId,
                ServiceId = booking.UserService.ServiceId, // Now safe
                CreatedAt = DateTime.UtcNow
            };

            _context.Reviews.Add(review);
            await _context.SaveChangesAsync();

            // Notify artist about the review
            var artistId = booking.UserService.ArtistId; // Now safe
            if (!string.IsNullOrEmpty(artistId))
            {
                await _notificationService.CreateNotificationAsync(
                    artistId,
                    "New Review Received! ⭐",
                    $"{currentUser.FirstName} left a {model.Rating}-star review for your service.",
                    "review",
                    booking.Id.ToString(),
                    Url.Action("Reviews", "Artist")
                );
            }

            TempData["Success"] = "Thank you for your review! It helps our artists grow.";
            return RedirectToAction("MyBookings", "Booking");
        }

        // GET: Review/MyReviews
        [HttpGet]
        public async Task<IActionResult> MyReviews()
        {
            var currentUser = await _userManager.GetUserAsync(User);

            var reviews = await _context.Reviews
                .Include(r => r.Booking)
                    .ThenInclude(b => b.UserService)
                        .ThenInclude(us => us.Service)
                .Include(r => r.Booking)
                    .ThenInclude(b => b.UserService.Artist)
                .Where(r => r.CustomerId == currentUser.Id)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return View(reviews);
        }
    }
}