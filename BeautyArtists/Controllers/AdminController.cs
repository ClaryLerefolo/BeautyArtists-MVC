using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using static BeautyArtists.Models.Booking;

namespace BeautyArtists.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _hostEnvironment;


        public AdminController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IWebHostEnvironment hostEnvironment)
        {
            _context = context;
            _userManager = userManager;
            _hostEnvironment = hostEnvironment;
        }

        public async Task<IActionResult> Index()
        {
            var model = new AdminDashboardViewModel
            {
                TotalUsers = await _userManager.Users.CountAsync(),
                TotalArtists = await _userManager.GetUsersInRoleAsync("Artist").ContinueWith(t => t.Result.Count),
                TotalCustomers = await _userManager.GetUsersInRoleAsync("Client").ContinueWith(t => t.Result.Count),
                TotalBookings = await _context.Bookings.CountAsync(),
                TotalRevenue = await _context.UserServices
                    .Join(_context.Bookings, us => us.Id, b => b.UserServiceId, (us, b) => us.Price)
                    .SumAsync(),
                RevenuePerArtist = await _context.UserServices
    .Include(us => us.Artist)
    .Join(_context.Bookings, us => us.Id, b => b.UserServiceId, (us, b) => new { us.ArtistId, us.Price, us.Artist })
    .GroupBy(x => x.ArtistId)
    .Select(g => new AdminDashboardViewModel.ArtistRevenue
    {
        ArtistId = g.Key,
        ArtistName = g.Select(x => x.Artist.FirstName + " " + x.Artist.LastName).FirstOrDefault(),
        TotalRevenue = g.Sum(x => x.Price)
    })
    .ToListAsync()
            };

            return View("Index", model);
        }

        public async Task<IActionResult> ManageUsers(string search)
        {
            var users = await _userManager.Users.ToListAsync();
            var userList = new List<UserManagementViewModel>();
            foreach (var user in users)
            {
                var role = (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? "None";

                // CHECK IF DEACTIVATED: If LockoutEnd is in the future, they are deactivated
                bool isDeactivated = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.Now;

                userList.Add(new UserManagementViewModel
                {
                    Id = user.Id,
                    FullName = $"{user.FirstName} {user.LastName}",
                    Email = user.Email,
                    Role = role,
                    IsDeactivated = isDeactivated // Make sure to add this prop to your ViewModel!
                });
            }
            var allServices = await _context.Services.ToListAsync();

            if (!string.IsNullOrEmpty(search))
            {
                userList = userList.Where(u =>
                    u.FullName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    u.Email.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            var masterModel = new UserManagementViewModel
            {
                Users = userList,
                Services = allServices
            };

            return View(masterModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleUserStatus(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            // If currently deactivated (locked out), reactivate them
            if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.Now)
            {
                await _userManager.SetLockoutEndDateAsync(user, null);
                TempData["Success"] = "User reactivated successfully.";
            }
            else
            {
                // Deactivate them by locking the account for 200 years
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.Now.AddYears(200));
                TempData["Error"] = "User deactivated.";
            }

            return RedirectToAction(nameof(ManageUsers));
        }

        public async Task<IActionResult> UserDetails(string id)
        {
            if (id == null) return NotFound();

            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            var role = (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? "None";

            var model = new UserManagementViewModel
            {
                Id = user.Id,
                FullName = $"{user.FirstName} {user.LastName}",
                Email = user.Email,
                Role = role
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction(nameof(ManageUsers));
            }

            await _userManager.DeleteAsync(user);
            TempData["Success"] = "User deleted successfully.";
            return RedirectToAction(nameof(ManageUsers));
        }


        public async Task<IActionResult> DeletePromotedAdmins()
        {
            var users = _userManager.Users.ToList();

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);

                // Check if the user is an Admin and should not be one (should be Artist or Client instead)
                if (roles.Contains("Admin") && (user.Role == "Artist" || user.Role == "Client"))
                {
                    // Delete the user if they shouldn't be an Admin
                    var result = await _userManager.DeleteAsync(user);

                    if (!result.Succeeded)
                    {
                        // Handle error (maybe log or display a message)
                        TempData["ErrorMessage"] = "An error occurred while deleting some users.";
                    }
                }
            }

            // Redirect to the user management page after deletion
            return RedirectToAction("Index", "Admin");
        }

        // Show all services
        //Show Service Creation Form
        public IActionResult CreateService()
        {
            var model = new ServiceViewModel
            {
                Categories = _context.ServiceCategories
                .OrderBy(c => c.Name)
                .Select(c => new SelectListItem
                {
                    Value = c.Id.ToString(),
                    Text = c.Name
                })
                .ToList()

            };

            return View(model);

        }
        // GET: Admin/ManageServices
        public async Task<IActionResult> ManageServices()
        {
            var services = await _context.Services
                .Include(s => s.ServiceCategory)
                .OrderBy(s => s.Name)
                .ToListAsync();
            return View(services);
        }

        // POST: Admin/CreateService
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateService(ServiceViewModel model, IFormFile? ImageFile)
        {
            if (!ModelState.IsValid)
            {
                model.Categories = _context.ServiceCategories
                    .OrderBy(c => c.Name)
                    .Select(c => new SelectListItem
                    {
                        Value = c.Id.ToString(),
                        Text = c.Name
                    }).ToList();
                return View(model);
            }

            var service = new Service
            {
                Name = model.Name,
                Description = model.Description,
                BasePrice = model.BasePrice,
                CategoryId = model.CategoryId,
                IsFeatured = model.IsFeatured
            };

            if (ImageFile != null && ImageFile.Length > 0)
            {
                string fileName = Guid.NewGuid() + Path.GetExtension(ImageFile.FileName);
                string uploadPath = Path.Combine(_hostEnvironment.WebRootPath, "images", "services");
                if (!Directory.Exists(uploadPath)) Directory.CreateDirectory(uploadPath);
                using var stream = new FileStream(Path.Combine(uploadPath, fileName), FileMode.Create);
                await ImageFile.CopyToAsync(stream);
                service.ImagePath = "/images/services/" + fileName;
            }

            _context.Services.Add(service);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Service created successfully.";
            return RedirectToAction(nameof(ManageServices));
        }

        // POST: Admin/EditService
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditService(ServiceViewModel model, IFormFile? ImageFile)
        {
            var service = await _context.Services.FindAsync(model.Id);
            if (service == null) return NotFound();

            service.Name = model.Name;
            service.Description = model.Description;
            service.BasePrice = model.BasePrice;
            service.CategoryId = model.CategoryId;
            service.IsFeatured = model.IsFeatured;

            if (ImageFile != null && ImageFile.Length > 0)
            {
                string fileName = Guid.NewGuid() + Path.GetExtension(ImageFile.FileName);
                string uploadPath = Path.Combine(_hostEnvironment.WebRootPath, "images", "services");
                if (!Directory.Exists(uploadPath)) Directory.CreateDirectory(uploadPath);
                using var stream = new FileStream(Path.Combine(uploadPath, fileName), FileMode.Create);
                await ImageFile.CopyToAsync(stream);
                service.ImagePath = "/images/services/" + fileName;
            }

            _context.Services.Update(service);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Service updated.";
            return RedirectToAction(nameof(ManageServices));
        }

        // POST: Admin/DeleteService
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteService(int id)
        {
            var service = await _context.Services.FindAsync(id);
            if (service == null) return NotFound();

            // Safety check — don't delete if artists are using it
            var inUse = await _context.UserServices.AnyAsync(us => us.ServiceId == id);
            if (inUse)
            {
                TempData["Error"] = "Cannot delete — this service is currently used by one or more artists.";
                return RedirectToAction(nameof(ManageServices));
            }

            _context.Services.Remove(service);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Service deleted successfully.";
            return RedirectToAction(nameof(ManageServices));
        }

        // POST: Admin/CreateService

        // Show edit form
        public async Task<IActionResult> EditService(int id)
        {
            var service = await _context.Services.FindAsync(id);
            if (service == null) return NotFound();

            var model = new ServiceViewModel
            {
                Id = service.Id,
                Name = service.Name,
                Description = service.Description,
                BasePrice = service.BasePrice,
                CategoryId = service.CategoryId,
                IsFeatured = service.IsFeatured,
                Categories = _context.ServiceCategories
                .OrderBy(c => c.Name)
                .Select(c => new SelectListItem
                {
                    Value = c.Id.ToString(),
                    Text = c.Name
                })
                .ToList()

            };

            return View(model);

        }
        public async Task<IActionResult> Revenue()
        {
            ViewData["Title"] = "Revenue";
            return View();
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
        public async Task<IActionResult> AuditLogs()
        {
            var logs = await _context.ActivityLogs
                .Include(a => a.Artist)
                .OrderByDescending(l => l.Timestamp)
                .ToListAsync();

            return View(logs);
        }
        // Save edited service

        public async Task<IActionResult> BookingDetails(int id)
        {
            var booking = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.Customer)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null) return NotFound();

            return View(booking);
        }
        // 1. THE VIEW: See all bookings across the whole shop
        public async Task<IActionResult> ManageBookings()
        {
            var allBookings = await _context.Bookings
                .Include(b => b.Customer) // Who is the client?
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service) // What service?
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist) // Which specific artist?
                .OrderByDescending(b => b.AppointmentDate)
                .ToListAsync();

            return View(allBookings);
        }

        // 2. THE MASTER OVERRIDE: Reverse or change any status
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminOverride(int bookingId, BookingStatus newStatus)
        {
            var booking = await _context.Bookings
                .Include(b => b.AvailabilitySlot)
                .Include(b => b.UserService)
                .FirstOrDefaultAsync(b => b.Id == bookingId);

            if (booking == null) return NotFound();

            // Admin has full freedom: Cancel, Approve (Confirmed), Completed, or Rejected
            booking.Status = newStatus;

            // SYNC THE CALENDAR: If Admin "Reverses" a rejection, we lock the slot again
            if (booking.AvailabilitySlot != null)
            {
                // The slot is only "Free" if the status is Cancelled or Rejected
                booking.AvailabilitySlot.IsBooked = (newStatus != BookingStatus.Cancelled &&
                                                    newStatus != BookingStatus.Rejected);
            }

            await _context.SaveChangesAsync();

            // LOG THE CEO INTERVENTION: So you have a paper trail
            await LogActivity(booking.UserService.ArtistId, $"ADMIN OVERRIDE: Forced status to {newStatus}");

            TempData["Success"] = "Booking status successfully overridden by Admin.";
            return RedirectToAction(nameof(ManageBookings));
        }

        public async Task<IActionResult> HeroBanners()
        {
            return View(await _context.HeroBanners.ToListAsync());
        }


        [HttpGet]
        public IActionResult CreateHeroBanner()
        {
            return View();
        }
        // 2. POST: Create Hero Banner
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateHeroBanner(HeroBanner banner, IFormFile imageFile)
        {
            if (imageFile != null && imageFile.Length > 0)
            {
                // Set the folder path: wwwroot/images/banners
                string wwwRootPath = _hostEnvironment.WebRootPath;
                string fileName = Guid.NewGuid().ToString() + Path.GetExtension(imageFile.FileName);
                string uploadPath = Path.Combine(wwwRootPath, @"images\banners");

                // Ensure directory exists
                if (!Directory.Exists(uploadPath)) Directory.CreateDirectory(uploadPath);

                // Save file to server
                using (var fileStream = new FileStream(Path.Combine(uploadPath, fileName), FileMode.Create))
                {
                    await imageFile.CopyToAsync(fileStream);
                }

                // Save the path to the database
                banner.ImagePath = "/images/banners/" + fileName;
            }

            _context.HeroBanners.Add(banner);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(HeroBanners));
        }

        // 1. GET: Load the banner data into the Edit View
        [HttpGet]
        public async Task<IActionResult> EditHeroBanner(int id)
        {
            var banner = await _context.HeroBanners.FindAsync(id);
            if (banner == null) return NotFound();

            return View(banner);
        }

        // 3. POST: Edit Hero Banner
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditHeroBanner(HeroBanner banner, IFormFile? imageFile)
        {
            var existingBanner = await _context.HeroBanners.AsNoTracking().FirstOrDefaultAsync(b => b.Id == banner.Id);
            if (existingBanner == null) return NotFound();

            if (imageFile != null && imageFile.Length > 0)
            {
                // New image upload logic
                string wwwRootPath = _hostEnvironment.WebRootPath;
                string fileName = Guid.NewGuid().ToString() + Path.GetExtension(imageFile.FileName);
                string uploadPath = Path.Combine(wwwRootPath, @"images\banners");

                using (var fileStream = new FileStream(Path.Combine(uploadPath, fileName), FileMode.Create))
                {
                    await imageFile.CopyToAsync(fileStream);
                }
                banner.ImagePath = "/images/banners/" + fileName;

                // Optional: Delete the old physical file here to keep the server clean
            }
            else
            {
                // Keep the existing image path if no new file was uploaded
                banner.ImagePath = existingBanner.ImagePath;
            }

            _context.HeroBanners.Update(banner);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(HeroBanners));
        }
    }
}
   



