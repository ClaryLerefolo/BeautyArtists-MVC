using BeautyArtists.Data;
using BeautyArtists.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BeautyArtists.Models.ViewModels;  // <-- add this
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.Blazor;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;
using Microsoft.AspNetCore.Mvc.Rendering;




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

            // Get artist name (fallback to email if username empty)
            var artistName = !string.IsNullOrEmpty(user.UserName) ? user.UserName : user.Email ?? "Artist";

            // Count portfolio items
            var portfolioCount = await _context.PortfolioItems
                .CountAsync(p => p.ArtistId == artistId);

            // Count artist services
            var servicesCount = await _context.UserServices
                .CountAsync(us => us.ArtistId == artistId);

            // Upcoming appointments count (Confirmed and in the future)
            var upcomingAppointmentsCount = await _context.Bookings
                .Where(b => b.UserService.ArtistId == artistId
                    && b.AppointmentDate> DateTime.Now
                    && b.Status == Booking.BookingStatus.Confirmed)
                .CountAsync();

            // Monthly earnings from completed bookings this month
            var monthlyEarnings = await _context.Bookings
                .Where(b => b.UserService.ArtistId == artistId
                    && b.AppointmentDate.Month == DateTime.Now.Month
                    && b.Status == Booking.BookingStatus.Confirmed)
                .SumAsync(b => b.UserService.Price);

            // Recent 5 appointments summary
            var recentAppointments = await _context.Bookings
                .Where(b => b.UserService.ArtistId == artistId)
                .OrderByDescending(b => b.AppointmentDate)
                .Take(5)
                .Select(b => new AppointmentSummary
                {
                    ClientName = b.Customer.UserName ?? b.Customer.Email,
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
                RecentAppointments = recentAppointments
            };

            return View(model);

        }


        // GET: View Profile
        public async Task<IActionResult> Profile()
        {
            var user = await _userManager.GetUserAsync(User);

            var profile = await _context.ArtistsProfiles
                .FirstOrDefaultAsync(p => p.UserId == user.Id);

            if (profile == null)
            {
                profile = new ArtistProfile
                {
                    UserId = user.Id,
                    FullName = $"{user.FirstName} {user.LastName}".Trim(),
                    Bio = "Tell us about yourself...",
                    YearsExperience = 0,
                    Location = "Unknown",
                    ContactInfo = "000-000-0000",
                    InstagramUrl = "",
                    ProfilePictureUrl = "/images/default-profile.png"
                };

                _context.ArtistsProfiles.Add(profile);
                await _context.SaveChangesAsync();
            }

            return View(profile);
        }

        // GET: Edit Profile
        public async Task<IActionResult> EditProfile()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var profile = await _context.ArtistsProfiles
                .FirstOrDefaultAsync(p => p.UserId == user.Id);

            if (profile == null)
            {
                return NotFound("Profile not found.");
            }

            return View(profile);
        }

        // POST: Edit Profile
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProfile(ArtistProfile updatedProfile, IFormFile? ProfilePictureFile)
        {
            var user = await _userManager.GetUserAsync(User);
            var profile = await _context.ArtistsProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);

            if (profile == null)
            {
                return NotFound();
            }

            // Update profile fields
            profile.FullName = string.IsNullOrWhiteSpace(updatedProfile.FullName)
                ? $"{user.FirstName} {user.LastName}".Trim()
                : updatedProfile.FullName;
            profile.Bio = updatedProfile.Bio;
            profile.YearsExperience = updatedProfile.YearsExperience;
            profile.Location = updatedProfile.Location;
            profile.ContactInfo = updatedProfile.ContactInfo;
            profile.InstagramUrl = updatedProfile.InstagramUrl;

            // Handle profile picture upload
            if (ProfilePictureFile != null && ProfilePictureFile.Length > 0)
            {
                var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads/profile_pictures");
                Directory.CreateDirectory(uploadsFolder);

                var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(ProfilePictureFile.FileName);
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await ProfilePictureFile.CopyToAsync(fileStream);
                }

                profile.ProfilePictureUrl = "/uploads/profile_pictures/" + uniqueFileName;
            }

            _context.ArtistsProfiles.Update(profile);
            await _context.SaveChangesAsync();

            return RedirectToAction("Profile");
        }
        // ========== Portfolio CRUD ==========


        // List all services the artist is offering
        public async Task<IActionResult> ManageServices()
        {
            var artist = await _userManager.GetUserAsync(User);

            var userServices = await _context.UserServices
                .Where(us => us.ArtistId == artist.Id)
                .Include(us => us.Service)
                    .ThenInclude(s => s.Category) // ✅ Include the Category
                .ToListAsync();

            return View(userServices); // ✅ Pass services to the view
        }

        private void LoadCategories()
{
    var categories = _context.PortfolioCategories
        .OrderBy(c => c.Name)
        .Select(c => new SelectListItem
        {
            Value = c.Id.ToString(),
            Text = c.Name
        })
        .ToList();

    categories.Insert(0, new SelectListItem { Value = "", Text = "-- Select Category --" });
    ViewBag.Categories = categories;
}


        // Show admin services artist hasn't picked yet
        public async Task<IActionResult> AddService()
        {
            var artist = await _userManager.GetUserAsync(User);

            // 🔧 Fix: Load category list for dropdown
            LoadCategories();

            var selectedServiceIds = await _context.UserServices
                .Where(us => us.ArtistId == artist.Id)
                .Select(us => us.ServiceId)
                .ToListAsync();

            var services = await _context.Services
                .Where(s => !selectedServiceIds.Contains(s.Id))
                .ToListAsync();

            var selectList = services.Select(s => new SelectListItem
            {
                Value = s.Id.ToString(),
                Text = $"{s.Name} ({s.Category}) - R{s.BasePrice}"
            }).ToList();

            var viewModel = new UserServiceViewModel
            {
                AvailableServices = selectList,
                ServiceId = services.FirstOrDefault()?.Id ?? 0,
                Price = 0
            };

            return View(viewModel);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddService(UserServiceViewModel model, List<IFormFile> Images)
        {
            var artist = await _userManager.GetUserAsync(User);

            if (!ModelState.IsValid)
            {
                LoadCategories();
                model.AvailableServices = await _context.Services
                    .Select(s => new SelectListItem
                    {
                        Value = s.Id.ToString(),
                        Text = $"{s.Name} - R{s.BasePrice}"
                    }).ToListAsync();
                return View(model);
            }

            var userService = new UserService
            {
                ArtistId = artist.Id,
                ServiceId = model.ServiceId,
                Price = model.Price,
                CustomDescription = model.CustomDescription,
                PortfolioCategoryId = model.PortfolioCategoryId > 0 ? model.PortfolioCategoryId : null
            };

            _context.UserServices.Add(userService);
            await _context.SaveChangesAsync();

            // Upload Images
            if (Images != null && Images.Count > 0)
            {
                var imageList = new List<ServiceImage>();
                foreach (var file in Images)
                {
                    if (file.Length > 0)
                    {
                        var path = Path.Combine("wwwroot/uploads/services", Guid.NewGuid().ToString() + Path.GetExtension(file.FileName));
                        using (var stream = new FileStream(path, FileMode.Create))
                        {
                            await file.CopyToAsync(stream);
                        }

                        imageList.Add(new ServiceImage
                        {
                            UserServiceId = userService.Id,
                            ImageUrl = "/uploads/services/" + Path.GetFileName(path)
                        });
                    }
                }

                _context.ServiceImages.AddRange(imageList);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(ManageServices));
        }


        [HttpGet]
        public async Task<IActionResult> GetServicesByCategory(int id)
        {
            var services = await _context.Services
                .Where(s => s.CategoryId == id)
                .Select(s => new SelectListItem
                {
                    Value = s.Id.ToString(),
                    Text = s.Name + $" (R{s.BasePrice})"
                })
                .ToListAsync();

            return Json(services);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddService(UserServiceViewModel model)
        {
            var artist = await _userManager.GetUserAsync(User);


            if (!ModelState.IsValid)
            {
                LoadCategories();                // ← add this line

                model.AvailableServices = await _context.Services
     .Select(s => new SelectListItem
     {
         Value = s.Id.ToString(),
         Text = $"{s.Name} ({s.Category}) - R{s.BasePrice}"
     }).ToListAsync();

                return View(model);
            }

            if (await _context.UserServices.AnyAsync(us => us.ArtistId == artist.Id && us.ServiceId == model.ServiceId))
            {
                TempData["Error"] = "Service already added.";
                return RedirectToAction(nameof(ManageServices));
            }

            var userService = new UserService
            {
                ArtistId = artist.Id,
                ServiceId = model.ServiceId,
                Price = model.Price,
                CustomDescription = model.CustomDescription,
                PortfolioCategoryId = model.PortfolioCategoryId > 0 ? model.PortfolioCategoryId : null



            };

            _context.UserServices.Add(userService);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(ManageServices));
        }

        // Edit pricing
        public async Task<IActionResult> EditUserService(int id)
        {
            var userService = await _context.UserServices
                .Include(us => us.Service)
                .FirstOrDefaultAsync(us => us.Id == id);

            if (userService == null) return NotFound();

            var viewModel = new EditUserServiceViewModel
            {
                Id = userService.Id,
                Price = userService.Price,
                CustomDescription = userService.CustomDescription,
                IsActive = userService.IsActive,
                ServiceName = userService.Service.Name,
                BasePrice = userService.Service.BasePrice
            };

            return View(viewModel);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditUserService(EditUserServiceViewModel model)
        {
            if (!ModelState.IsValid)
            {
                // Re-fetch service info to re-display admin pricing and name
                var service = await _context.UserServices
                    .Include(us => us.Service)
                    .Where(us => us.Id == model.Id)
                    .Select(us => us.Service)
                    .FirstOrDefaultAsync();

                if (service != null)
                {
                    model.ServiceName = service.Name;
                    model.BasePrice = service.BasePrice;
                }

                return View(model); // Return view with fixed info


            }

            var userService = await _context.UserServices
                .FirstOrDefaultAsync(us => us.Id == model.Id);

            if (userService == null) return NotFound();

            userService.Price = model.Price;
            userService.Duration = model.Duration;
            userService.CustomDescription = model.CustomDescription;
            userService.IsActive = model.IsActive;


            await _context.SaveChangesAsync();

            TempData["Success"] = "Service pricing updated successfully.";
            return RedirectToAction(nameof(ManageServices));
        }



        // Remove a service
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUserService(int id)
        {
            var userService = await _context.UserServices.FindAsync(id);
            if (userService != null)
            {
                _context.UserServices.Remove(userService);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(ManageServices));
        }

        // View all appointments related to the current artist
        public async Task<IActionResult> Appointments()
        {
            var artist = await _userManager.GetUserAsync(User);

            var bookings = await _context.Bookings
                .Include(b => b.Customer)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Where(b => b.UserService.ArtistId == artist.Id)
                .ToListAsync();

            return View(bookings);
        }

        // GET: Booking Details for Artist
        public async Task<IActionResult> BookingDetails(int id)
        {
            var booking = await _context.Bookings
                .Include(b => b.Customer)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService.Artist)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null) return NotFound();

            return View(booking);
        }

        // POST: Update Booking Status (shared by both views)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateBookingStatus(int bookingId, string newStatus)
        {
            if (!Enum.TryParse<Booking.BookingStatus>(newStatus, out var status))
            {
                TempData["Error"] = "Invalid status.";
                return RedirectToAction(nameof(Appointments));
            }

            var booking = await _context.Bookings.FindAsync(bookingId);
            if (booking == null)
            {
                TempData["Error"] = "Booking not found.";
                return RedirectToAction(nameof(Appointments));
            }

            booking.Status = status;
            await _context.SaveChangesAsync();

            TempData["Success"] = "Booking status updated.";
            return RedirectToAction(nameof(Appointments));
        }

    }
}
