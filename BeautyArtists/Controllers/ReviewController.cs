using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Models.ViewModels;
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
        private readonly ILogger<ReviewController> _logger;

        public ReviewController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            ILogger<ReviewController> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        // GET: Review/Create/5
        [HttpGet]
        public async Task<IActionResult> Create(int bookingId)
        {
            try
            {
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null)
                    return Challenge();

                var booking = await _context.Bookings
                    .Include(b => b.UserService)
                        .ThenInclude(us => us.Service)
                    .Include(b => b.UserService)
                        .ThenInclude(us => us.Artist)
                    .FirstOrDefaultAsync(b => b.Id == bookingId && b.CustomerId == currentUser.Id);

                if (booking == null)
                {
                    _logger.LogWarning($"Booking {bookingId} not found");
                    return NotFound();
                }

                if (booking.UserService == null)
                {
                    TempData["Error"] = "This booking has missing service details. Please contact support.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                if (booking.Status != BookingStatus.Completed)
                {
                    TempData["Error"] = "You can only review completed services.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                if (await _context.Reviews.AnyAsync(r => r.BookingId == bookingId))
                {
                    TempData["Error"] = "You have already reviewed this service.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                var model = new ReviewViewModel
                {
                    BookingId = booking.Id,
                    ServiceName = booking.UserService?.Service?.Name ?? "Service",
                    ArtistName = booking.UserService?.Artist != null
                        ? $"{booking.UserService.Artist.FirstName} {booking.UserService.Artist.LastName}".Trim()
                        : "Artist",
                    AppointmentDate = booking.AppointmentDate
                };

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in Create GET for booking {bookingId}");
                TempData["Error"] = "An error occurred. Please try again.";
                return RedirectToAction("MyBookings", "Booking");
            }
        }

        // POST: Review/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ReviewViewModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                    return View(model);

                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null)
                    return Challenge();

                var booking = await _context.Bookings
                    .Include(b => b.UserService)
                        .ThenInclude(us => us.Service)
                    .Include(b => b.UserService)
                        .ThenInclude(us => us.Artist)
                    .FirstOrDefaultAsync(b => b.Id == model.BookingId && b.CustomerId == currentUser.Id);

                if (booking == null)
                {
                    _logger.LogWarning($"Booking {model.BookingId} not found");
                    return NotFound();
                }

                if (booking.UserService == null)
                {
                    TempData["Error"] = "This booking has missing service details. Please contact support.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                if (await _context.Reviews.AnyAsync(r => r.BookingId == model.BookingId))
                {
                    TempData["Error"] = "You have already reviewed this service.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                // 🔥 Make sure ServiceId is valid
                int serviceId = booking.UserService.ServiceId;
                if (serviceId <= 0)
                {
                    _logger.LogWarning($"Invalid ServiceId {serviceId} for booking {model.BookingId}");
                    TempData["Error"] = "Unable to save review: invalid service data.";
                    return RedirectToAction("MyBookings", "Booking");
                }

                var review = new Review
                {
                    Rating = model.Rating,
                    Comment = model.Comment ?? string.Empty,
                    CustomerId = currentUser.Id,
                    BookingId = model.BookingId,
                    ServiceId = serviceId,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Reviews.Add(review);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"✅ Review saved for booking {model.BookingId}");

                TempData["Success"] = "Thank you for your review! It helps our artists grow.";
                return RedirectToAction("MyBookings", "Booking");
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, $"Database error saving review for booking {model.BookingId}");
                TempData["Error"] = "Database error occurred. Please try again.";
                return RedirectToAction("MyBookings", "Booking");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in Create POST for booking {model.BookingId}");
                TempData["Error"] = "An error occurred while saving your review. Please try again.";
                return RedirectToAction("MyBookings", "Booking");
            }
        }

        // GET: Review/MyReviews
        [HttpGet]
        public async Task<IActionResult> MyReviews()
        {
            try
            {
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null)
                    return Challenge();

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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in MyReviews");
                TempData["Error"] = "An error occurred loading your reviews.";
                return RedirectToAction("Index", "Home");
            }
        }
    }
}