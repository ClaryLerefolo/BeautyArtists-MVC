using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using static BeautyArtists.Models.Booking;

namespace BeautyArtists.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public AdminController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
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
                    .Join(_context.Bookings, us => us.Id, b => b.UserServiceId, (us, b) => new { us.ArtistId, us.Price })
                    .GroupBy(x => x.ArtistId)
                    .Select(g => new AdminDashboardViewModel.ArtistRevenue
                    {
                        ArtistId = g.Key,
                        TotalRevenue = g.Sum(x => x.Price)
                    })
                    .ToListAsync()
            };

            return View("Index", model);
        }

        public async Task<IActionResult> ManageUsers(string search)
        {
            var users = await _userManager.Users.ToListAsync();
            var result = new List<UserManagementViewModel>();

            foreach (var user in users)
            {
                var role = (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? "None";
                result.Add(new UserManagementViewModel
                {
                    Id = user.Id,
                    FullName = $"{user.FirstName} {user.LastName}",
                    Email = user.Email,
                    Role = role
                });
            }

            if (!string.IsNullOrEmpty(search))
            {
                result = result.Where(u =>
                    u.FullName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    u.Email.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return View(result);
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
        public async Task<IActionResult> Services()
        {

           var services = await _context.Services
                .Include(s => s.Category)
                .ToListAsync();

            return View(services);
        }
      


        //Show Service Creation Form
        public IActionResult CreateService()
        {
            var model = new ServiceViewModel
            {
                Categories = _context.PortfolioCategories
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


        // POST: Admin/CreateService
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateService(ServiceViewModel model)
        {
            if (!ModelState.IsValid)
            {
                model.Categories = _context.PortfolioCategories
                    .OrderBy(c => c.Name)
                    .Select(c => new SelectListItem
                    {
                        Value = c.Id.ToString(),
                        Text = c.Name
                    })
                    .ToList();

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
            if (string.IsNullOrEmpty(service.ImagePath))
            {
                service.ImagePath = "/uploads/services/default.jpg"; // or any default path
            }


            _context.Services.Add(service);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Services));
        }

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
                Categories = _context.PortfolioCategories
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

        // Save edited service
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditService(ServiceViewModel model)
        {
            if (!ModelState.IsValid)
            {
                model.Categories = _context.PortfolioCategories
                    .OrderBy (c => c.Name)
                    .Select(c => new SelectListItem
                    {
                        Value = c.Id.ToString(),
                        Text = c.Name
                    }).ToList();
            }
            var service = await _context.Services.FindAsync(model.Id);
            if (service == null) return NotFound();

            service.Name = model.Name;
            service.Description = model.Description;
            service.BasePrice = model.BasePrice;
            service.CategoryId = model.CategoryId;
            service.IsFeatured = model.IsFeatured;

            _context.Services.Add(service);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Services));
        }

        // Delete service
        [HttpPost]
        public async Task<IActionResult> DeleteService(int id)
        {
            var service = await _context.Services.FindAsync(id);
            if (service != null)
            {
                _context.Services.Remove(service);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Services));
        }
        // Manage Bookings
        public async Task<IActionResult> ManageBookings(string search)
        {
            var bookings = _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.Customer)
                .AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                bookings = bookings.Where(b =>
                    b.Customer.FullName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    b.UserService.Artist.FullName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    b.UserService.Service.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                );
            }

            var bookingList = await bookings.ToListAsync();
            return View(bookingList);
        }

        // View Booking Details
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

        // Update Booking Status
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateBookingStatus(int bookingId, string status)
        {
            var booking = await _context.Bookings.FindAsync(bookingId);

            if (booking == null)
            {
                return NotFound();
            }

            // Parse the string to BookingStatus enum value (if valid)
            if (Enum.TryParse<BookingStatus>(status, true, out var parsedStatus))
            {
                booking.Status = parsedStatus;
                await _context.SaveChangesAsync();
            }
            else
            {
                TempData["Error"] = "Invalid booking status.";
            }

            return RedirectToAction(nameof(ManageBookings));  // Redirect after updating
        }


        // Delete Booking
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteBooking(int id)
        {
            var booking = await _context.Bookings.FindAsync(id);
            if (booking != null)
            {
                _context.Bookings.Remove(booking);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Booking deleted successfully.";
            }

            return RedirectToAction(nameof(ManageBookings));
        }



    }
}
