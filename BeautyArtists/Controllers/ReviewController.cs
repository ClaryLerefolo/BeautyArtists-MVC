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
        private readonly ILogger<ReviewController> _logger;


        public ReviewController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            INotificationService notificationService, ILogger<ReviewController> logger)
        {
            _context = context;
            _userManager = userManager;
            _notificationService = notificationService;
            _logger = logger;

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
                return View(model);

            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
                return Challenge();

            // Load booking with all needed includes
            var booking = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                .FirstOrDefaultAsync(b => b.Id == model.BookingId && b.CustomerId == currentUser.Id);

            if (booking == null)
                return NotFound();

            // Guard against missing UserService (data integrity)
            if (booking.UserService == null)
            {
                TempData["Error"] = "This booking has missing service details. Please contact support.";
                return RedirectToAction("MyBookings", "Booking");
            }

            // Check if already reviewed
            if (await _context.Reviews.AnyAsync(r => r.BookingId == model.BookingId))
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
                ServiceId = booking.UserService.ServiceId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Reviews.Add(review);
            await _context.SaveChangesAsync();

            // ============================================================
            // 🔥 SAFE NOTIFICATION – NEVER LET IT BREAK THE RESPONSE
            // ============================================================
            try
            {
                var artistId = booking.UserService.ArtistId;
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
            }
            catch (Exception ex)
            {
                // Log the error (Azure will capture this in Log Stream)
                _logger.LogError(ex, "Failed to send notification for review on booking {BookingId}", booking.Id);
                // We do NOT rethrow – the review is already saved.
            }

            TempData["Success"] = "Thank you for your review! It helps our artists grow.";
            return RedirectToAction("MyBookings", "Booking");
        }

        // GET: Review/MyReviews
        [HttpGet]
        public async Task<IActionResult> MyReviews()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
                return Challenge();

            // Load all reviews with booking and related data
            var reviews = await _context.Reviews
                .Include(r => r.Booking)
                    .ThenInclude(b => b.UserService)
                        .ThenInclude(us => us.Service)
                .Include(r => r.Booking)
                    .ThenInclude(b => b.UserService)
                        .ThenInclude(us => us.Artist)
                            .ThenInclude(a => a.ArtistProfile)
                .Where(r => r.CustomerId == currentUser.Id)
                .OrderByDescending(r => r.CreatedAt)
                .AsNoTracking()
                .ToListAsync();

            // Build the view model with safe null handling
            var model = new MyReviewsViewModel
            {
                Reviews = reviews.Select(r => new ReviewWithDetails
                {
                    Review = r,
                    ServiceName = r.Booking?.UserService?.Service?.Name ?? "Service",
                    ArtistName = r.Booking?.UserService?.Artist?.ArtistProfile?.FullName
                        ?? (!string.IsNullOrEmpty(r.Booking?.UserService?.Artist?.FirstName)
                            ? $"{r.Booking.UserService.Artist.FirstName} {r.Booking.UserService.Artist.LastName}".Trim()
                            : r.Booking?.UserService?.Artist?.UserName ?? "Artist"),
                    ArtistProfilePicture = r.Booking?.UserService?.Artist?.ArtistProfile?.ProfilePictureUrl
                        ?? "/images/default-profile.png",
                    AppointmentDate = r.Booking?.AppointmentDate.ToString("dddd, dd MMM yyyy")
                        ?? DateTime.Now.ToString("dddd, dd MMM yyyy"),
                        ArtistId = r.Booking?.UserService?.ArtistId

                }).ToList()
            };

            return View(model);
        }
    }
}