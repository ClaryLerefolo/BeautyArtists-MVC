using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using static BeautyArtists.Models.Booking;

namespace BeautyArtists.Controllers
{
    [Authorize(Roles = "Artist")]
    public class ArtistController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _env;

        public ArtistController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IWebHostEnvironment env)
        {
            _context = context;
            _userManager = userManager;
            _env = env;
        }

        public async Task<IActionResult> Dashboard()
        {
            var user = await _userManager.GetUserAsync(User);
            var artistId = user.Id;

            var artistName = !string.IsNullOrEmpty(user.FullName) ? user.FullName : $"{user.FirstName} {user.LastName}";
            if (string.IsNullOrWhiteSpace(artistName)) { artistName = user.Email ?? "Artist"; }

            var portfolioCount = await _context.PortfolioItems.CountAsync(p => p.ArtistId == artistId);
            var servicesCount = await _context.UserServices.CountAsync(us => us.ArtistId == artistId);

            var upcomingAppointmentsCount = await _context.Bookings
                .Where(b => b.UserService.ArtistId == artistId
                    && b.AppointmentDate > DateTime.Now
                    && b.Status == Booking.BookingStatus.Confirmed)
                .CountAsync();

            var monthlyEarnings = await _context.Bookings
                .Where(b => b.UserService.ArtistId == artistId
                    && b.AppointmentDate.Month == DateTime.Now.Month
                    && b.AppointmentDate.Year == DateTime.Now.Year
                    && b.Status == Booking.BookingStatus.Confirmed)
                .SumAsync(b => (decimal?)b.UserService.Price) ?? 0;

            var chartData = new List<decimal>();
            var chartLabels = new List<string>();

            for (int i = 5; i >= 0; i--)
            {
                var monthDate = DateTime.Now.AddMonths(-i);
                var monthSum = await _context.Bookings
                    .Where(b => b.UserService.ArtistId == artistId
                           && b.Status == Booking.BookingStatus.Confirmed
                           && b.AppointmentDate.Month == monthDate.Month
                           && b.AppointmentDate.Year == monthDate.Year)
                    .SumAsync(b => (decimal?)b.UserService.Price) ?? 0;

                chartData.Add(monthSum);
                chartLabels.Add(monthDate.ToString("MMM"));
            }

            var recentAppointments = await _context.Bookings
                .Where(b => b.UserService.ArtistId == artistId)
                .OrderByDescending(b => b.AppointmentDate)
                .Take(4)
                .Select(b => new AppointmentSummary
                {
                    ClientName = !string.IsNullOrEmpty(b.Customer.FirstName)
                                 ? $"{b.Customer.FirstName} {b.Customer.LastName}"
                                 : b.Customer.Email,
                    ServiceName = b.UserService.Service.Name,
                    AppointmentDate = b.AppointmentDate,
                    Status = b.Status.ToString()
                })
                .ToListAsync();

            var model = new ArtistDashboardViewModel
            {
                ArtistName = artistName,
                PortfolioItemsCount = portfolioCount,
                ServicesCount = servicesCount,
                UpcomingAppointments = upcomingAppointmentsCount,
                MonthlyEarnings = monthlyEarnings,
                RecentAppointments = recentAppointments,
                MonthlyEarningsGraph = chartData,
                MonthlyLabels = chartLabels
            };

            return View(model);
        }

        public async Task<IActionResult> Profile()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            ViewData["Title"] = "Profile";

            var profile = await _context.ArtistProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);

            if (profile == null)
            {
                profile = new ArtistProfile
                {
                    UserId = user.Id,
                    FullName = $"{user.FirstName} {user.LastName}".Trim(),
                    Bio = "Professional Artist",
                    YearsExperience = 0,
                    Province = "Gauteng",
                    City = "Johannesburg",
                    ContactInfo = "000-000-0000",
                    InstagramUrl = "",
                    ProfilePictureUrl = "/images/default-profile.png"
                };

                _context.ArtistProfiles.Add(profile);
                await _context.SaveChangesAsync();
            }
            return View(profile);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProfile(ArtistProfile updatedProfile, IFormFile? ProfilePicture)
        {
            ModelState.Clear();

            var user = await _userManager.GetUserAsync(User);
            var profile = await _context.ArtistProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);

            if (profile == null) return NotFound();

            // Update text data
            profile.FullName = updatedProfile.FullName;
            profile.Bio = updatedProfile.Bio;
            profile.YearsExperience = updatedProfile.YearsExperience;
            profile.Province = updatedProfile.Province;
            profile.City = updatedProfile.City;
            profile.ContactInfo = updatedProfile.ContactInfo;
            profile.InstagramUrl = updatedProfile.InstagramUrl;

            // Update image if provided
            if (ProfilePicture != null && ProfilePicture.Length > 0)
            {
                var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads/profile_pictures");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(ProfilePicture.FileName);
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                    await ProfilePicture.CopyToAsync(fileStream);
                profile.ProfilePictureUrl = "/uploads/profile_pictures/" + uniqueFileName;
            }

            // ← REMOVE ModelState.IsValid check — just save directly
            // Navigation properties like User, Services cause ModelState to fail
            _context.ArtistProfiles.Update(profile);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Profile updated successfully!";
            return RedirectToAction(nameof(Profile));
        }

        [HttpPost]
        public async Task<IActionResult> UpdatePicture(IFormFile ProfilePicture)
        {
            var user = await _userManager.GetUserAsync(User);
            var profile = await _context.ArtistProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);

            if (profile != null && ProfilePicture != null && ProfilePicture.Length > 0)
            {
                var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads/profile_pictures");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(ProfilePicture.FileName);
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await ProfilePicture.CopyToAsync(fileStream);
                }

                profile.ProfilePictureUrl = "/uploads/profile_pictures/" + uniqueFileName;
                _context.ArtistProfiles.Update(profile);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Profile));
        }

        public async Task<IActionResult> ManageServices()
        {
            var userId = _userManager.GetUserId(User);

            var userServices = await _context.UserServices
                .Include(us => us.Service)
                    .ThenInclude(s => s.ServiceCategory)
                .Where(us => us.ArtistId == userId)
                .ToListAsync();

            var available = await _context.Services
                .Include(s => s.ServiceCategory)
                .ToListAsync();

            ViewBag.AvailableServices = available.Select(s => new SelectListItem
            {
                Value = s.Id.ToString(),
                Text = s.Name
            }).ToList();

            // MATCHING THE JAVASCRIPT STRUCTURE EXACTLY
            ViewBag.RawServices = available.Select(s => new {
                id = s.Id,
                name = s.Name,
                // This object structure is what your JS (s.serviceCategory.name) requires
                serviceCategory = new { name = s.ServiceCategory?.Name ?? "General" },
                basePrice = s.BasePrice,
                duration = s.Duration,
                description = s.Description
            }).ToList();

            return View(userServices);
        }
     
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddService(UserService model, IFormFile? ImageFile)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Challenge();

            // 1. Check if this Service actually exists in the Admin's table
            var baseService = await _context.Services.FindAsync(model.ServiceId);
            if (baseService == null)
            {
                TempData["Error"] = "Invalid Service Selection. Please try again.";
                return RedirectToAction(nameof(ManageServices));
            }

            // 2. Set the ID manually
            model.ArtistId = userId;
            model.IsActive = true;

            // 3. FORCE CLEAR everything except the raw data fields
            // We don't want EF trying to validate the 'Service' or 'Artist' objects
            ModelState.Clear();
            model.IsActive = Request.Form["IsActive"].Contains("true"); // ADD THIS

            // Re-validate only the fields we actually care about
            if (model.Price <= 0 || model.Duration <= 0)
            {
                TempData["Error"] = "Price and Duration must be greater than zero.";
                return RedirectToAction(nameof(ManageServices));
            }

            try
            {
                // 4. Handle Image
                if (ImageFile != null && ImageFile.Length > 0)
                {
                    var uploadDir = Path.Combine(_env.WebRootPath, "uploads/services");
                    if (!Directory.Exists(uploadDir)) Directory.CreateDirectory(uploadDir);

                    var fileName = $"{Guid.NewGuid()}_{Path.GetFileName(ImageFile.FileName)}";
                    var filePath = Path.Combine(uploadDir, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await ImageFile.CopyToAsync(stream);
                    }
                    model.ImagePath = "/uploads/services/" + fileName;
                }

                // 5. THE SAVE
                _context.UserServices.Add(model);
                await _context.SaveChangesAsync();

                TempData["Success"] = "Service added successfully!";
            }
            catch (Exception ex)
            {
                // This is the "Truth Serum" - it will tell us exactly what column is failing
                var dbError = ex.InnerException?.Message ?? ex.Message;
                TempData["Error"] = "Database Refused Save: " + dbError;
            }

            return RedirectToAction(nameof(ManageServices));
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditUserService(UserService model, IFormFile? ImageFile)
        {
            // FIND the actual record first
            var existingService = await _context.UserServices.FindAsync(model.Id);
            if (existingService == null) return NotFound();

            // MANDATORY: Remove these from validation because they aren't in the Edit form
            // If you don't do this, ModelState.IsValid will ALWAYS be false.
            ModelState.Remove("ArtistId");
            ModelState.Remove("ServiceId");
            ModelState.Remove("Artist");
            ModelState.Remove("Service");
            ModelState.Remove("ImagePath");

            if (ModelState.IsValid)
            {
                try
                {
                    // 1. Handle Image Update
                    if (ImageFile != null && ImageFile.Length > 0)
                    {
                        if (!string.IsNullOrEmpty(existingService.ImagePath))
                        {
                            var oldPath = Path.Combine(_env.WebRootPath, existingService.ImagePath.TrimStart('/'));
                            if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
                        }

                        var fileName = $"{Guid.NewGuid()}_{Path.GetFileName(ImageFile.FileName)}";
                        var uploadDir = Path.Combine(_env.WebRootPath, "uploads/services");
                        if (!Directory.Exists(uploadDir)) Directory.CreateDirectory(uploadDir);

                        var filePath = Path.Combine(uploadDir, fileName);
                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await ImageFile.CopyToAsync(stream);
                        }
                        existingService.ImagePath = "/uploads/services/" + fileName;
                    }

                    // 2. Update Text Fields - Apply changes to the EXISTING tracked entity
                    existingService.Price = model.Price;
                    existingService.Duration = model.Duration;
                    existingService.CustomDescription = model.CustomDescription;
                    existingService.IsActive = model.IsActive;

                    _context.Update(existingService);
                    await _context.SaveChangesAsync();

                    TempData["Success"] = "Service updated successfully!";
                    return RedirectToAction(nameof(ManageServices));
                }
                catch (Exception ex)
                {
                    TempData["Error"] = "Update Failed: " + (ex.InnerException?.Message ?? ex.Message);
                }
            }
            else
            {
                // If we hit here, validation failed. Let's see why in the debug console.
                var errors = string.Join(" | ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                TempData["Error"] = "Validation Error: " + errors;
            }

            return RedirectToAction(nameof(ManageServices));
        }
        private async Task LogActivity(string artistId, string message)
        {
            var log = new ActivityLog
            {
                ArtistId = artistId,
                Action = message, // This matches your Model's 'Action' property
                Description = $"Log generated at {DateTime.Now}", // Optional detail
                Timestamp = DateTime.Now
            };

            _context.ActivityLogs.Add(log);
            await _context.SaveChangesAsync();
        }
        [HttpPost]
        public async Task<IActionResult> DeleteUserService(int id)
        {
            var userService = await _context.UserServices.FindAsync(id);
            if (userService == null) return NotFound();

            try
            {
                // 1. Delete the physical image file from the server
                if (!string.IsNullOrEmpty(userService.ImagePath))
                {
                    var filePath = Path.Combine(_env.WebRootPath, userService.ImagePath.TrimStart('/'));
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }
                }

                // 2. Remove from Database
                _context.UserServices.Remove(userService);
                await _context.SaveChangesAsync();

                return Ok(); // Returns 200 OK to the JavaScript
            }
            catch (Exception)
            {
                return BadRequest("Could not delete service.");
            }


        }
        // This shows ONLY the bookings for the logged-in Artist
        public async Task<IActionResult> MyAppointments()
        {
            var artistId = _userManager.GetUserId(User);

            var myBookings = await _context.Bookings
                .Include(b => b.Customer)
                .Include(b => b.UserService).ThenInclude(us => us.Service)
                .Where(b => b.UserService.ArtistId == artistId) // The Filter
                .OrderByDescending(b => b.AppointmentDate)
                .ToListAsync();

            return View(myBookings);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ArtistUpdateStatus(int bookingId,
     Booking.BookingStatus newStatus, string? artistNotes)
        {
            var artistId = _userManager.GetUserId(User);
            var booking = await _context.Bookings
                .Include(b => b.AvailabilitySlot)
                .Include(b => b.UserService)
                .FirstOrDefaultAsync(b => b.Id == bookingId && b.UserService.ArtistId == artistId);

            if (booking == null) return Unauthorized();

            booking.Status = newStatus;
            booking.ArtistNotes = artistNotes; // ← save the note

            if (booking.AvailabilitySlot != null)
            {
                booking.AvailabilitySlot.IsBooked = (newStatus != Booking.BookingStatus.Cancelled &&
                                                     newStatus != Booking.BookingStatus.Rejected);
            }

            await _context.SaveChangesAsync();
            await LogActivity(artistId, $"Artist updated Booking #{bookingId} to {newStatus}");
            return RedirectToAction(nameof(MyAppointments));
        }


    }
}