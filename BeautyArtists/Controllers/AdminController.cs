using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Models.ViewModels;
using BeautyArtists.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using static BeautyArtists.Models.Booking;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;

namespace BeautyArtists.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _hostEnvironment;
        private readonly ICommunicationService _commService;
        private readonly IPaystackService _paystackService;
        private readonly IEmailSender _emailSender;

        // ─── PRICING CONSTANTS ───
        private const decimal CLIENT_MARKUP_RATE = 0.04m;
        private const decimal BOOKING_FEE = 5.00m;
        private const decimal NEW_CLIENT_COMMISSION = 0.10m;
        private const decimal REPEAT_CLIENT_FLAT_FEE = 15.00m;
        private const decimal MIN_PLATFORM_FEE = 8.00m;

        public AdminController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment hostEnvironment,
            ICommunicationService commService,
            IPaystackService paystackService,
            IEmailSender emailSender)
        {
            _context = context;
            _userManager = userManager;
            _hostEnvironment = hostEnvironment;
            _commService = commService;
            _paystackService = paystackService;
            _emailSender = emailSender;
        }

        // ─── HELPERS ───
        private async Task<bool> IsNewClient(string customerId, int userServiceId)
        {
            var existingBookings = await _context.Bookings
                .Where(b => b.CustomerId == customerId
                            && b.UserServiceId == userServiceId
                            && b.Status != BookingStatus.Cancelled
                            && b.Status != BookingStatus.Rejected)
                .AnyAsync();

            return !existingBookings;
        }

        private decimal GetPlatformFee(decimal servicePrice, bool isNewClient)
        {
            var platformFee = isNewClient
                ? servicePrice * NEW_CLIENT_COMMISSION
                : REPEAT_CLIENT_FLAT_FEE;

            return Math.Max(platformFee, MIN_PLATFORM_FEE);
        }

        private decimal GetArtistPayout(decimal servicePrice, bool isNewClient)
        {
            return servicePrice - GetPlatformFee(servicePrice, isNewClient);
        }

        private decimal GetClientTotal(decimal servicePrice)
        {
            return (servicePrice * (1 + CLIENT_MARKUP_RATE)) + BOOKING_FEE;
        }

        // ═══════════════════════════════════════════════════════════
        // DASHBOARD
        // ═══════════════════════════════════════════════════════════
        public async Task<IActionResult> Index()
        {
            var model = new AdminDashboardViewModel
            {
                TotalUsers = await _userManager.Users.CountAsync(),
                TotalArtists = await _userManager.GetUsersInRoleAsync("Artist").ContinueWith(t => t.Result.Count),
                TotalCustomers = await _userManager.GetUsersInRoleAsync("Client").ContinueWith(t => t.Result.Count),
                TotalBookings = await _context.Bookings.CountAsync(),
                TotalRevenue = await CalculateTotalPlatformEarnings(),
                RevenuePerArtist = await CalculateRevenuePerArtist()
            };

            return View("Index", model);
        }

        private async Task<decimal> CalculateTotalPlatformEarnings()
        {
            var completedBookings = await _context.Bookings
                .Include(b => b.UserService)
                .Where(b => b.Status == BookingStatus.Completed)
                .ToListAsync();

            decimal total = 0m;
            foreach (var booking in completedBookings)
            {
                bool isNew = await IsNewClient(booking.CustomerId, booking.UserServiceId);
                decimal platformFee = GetPlatformFee(booking.ServicePrice, isNew);
                decimal markup = booking.ServicePrice * CLIENT_MARKUP_RATE;
                total += markup + platformFee + booking.BookingFee;
            }

            return total;
        }

        private async Task<List<AdminDashboardViewModel.ArtistRevenue>> CalculateRevenuePerArtist()
        {
            var completedBookings = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                .Where(b => b.Status == BookingStatus.Completed)
                .ToListAsync();

            var result = new Dictionary<string, AdminDashboardViewModel.ArtistRevenue>();

            foreach (var booking in completedBookings)
            {
                var artistId = booking.UserService.ArtistId;
                var artistName = $"{booking.UserService.Artist.FirstName} {booking.UserService.Artist.LastName}".Trim();

                if (!result.ContainsKey(artistId))
                {
                    result[artistId] = new AdminDashboardViewModel.ArtistRevenue
                    {
                        ArtistId = artistId,
                        ArtistName = artistName,
                        TotalRevenue = 0m
                    };
                }

                bool isNew = await IsNewClient(booking.CustomerId, booking.UserServiceId);
                decimal platformFee = GetPlatformFee(booking.ServicePrice, isNew);
                decimal markup = booking.ServicePrice * CLIENT_MARKUP_RATE;
                result[artistId].TotalRevenue += markup + platformFee + booking.BookingFee;
            }

            return result.Values.ToList();
        }

        // ═══════════════════════════════════════════════════════════
        // MANAGE USERS - paginated
        // ═══════════════════════════════════════════════════════════
        public async Task<IActionResult> ManageUsers(string search, string role, int page = 1, int pageSize = 10)
        {
            var users = await _userManager.Users.ToListAsync();
            var userList = new List<UserManagementViewModel>();
            foreach (var user in users)
            {
                var userRole = (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? "None";
                bool isDeactivated = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.Now;

                userList.Add(new UserManagementViewModel
                {
                    Id = user.Id,
                    FullName = $"{user.FirstName} {user.LastName}",
                    Email = user.Email,
                    Role = userRole,
                    IsDeactivated = isDeactivated,
                    IsEmailConfirmed = user.EmailConfirmed
                });
            }
            var allServices = await _context.Services.ToListAsync();

            int totalAdmins = userList.Count(u => u.Role == "Admin");
            int totalArtists = userList.Count(u => u.Role == "Artist");
            int totalClients = userList.Count(u => u.Role == "Client");
            int totalUsers = userList.Count;

            if (!string.IsNullOrEmpty(search))
            {
                userList = userList.Where(u =>
                    u.FullName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    u.Email.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!string.IsNullOrEmpty(role))
            {
                userList = userList.Where(u => u.Role == role).ToList();
            }

            userList = userList
                .OrderBy(u => u.Role == "Admin" ? 0 : u.Role == "Artist" ? 1 : u.Role == "Client" ? 2 : 3)
                .ThenBy(u => u.FullName)
                .ToList();

            int totalCount = userList.Count;
            int totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            if (totalPages < 1) totalPages = 1;
            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            var paged = userList
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var masterModel = new UserManagementViewModel
            {
                Users = paged,
                Services = allServices
            };

            ViewBag.Search = search;
            ViewBag.Role = role;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;
            ViewBag.PageSize = pageSize;
            ViewBag.ShowingFrom = totalCount == 0 ? 0 : ((page - 1) * pageSize) + 1;
            ViewBag.ShowingTo = Math.Min(page * pageSize, totalCount);
            ViewBag.TotalUsers = totalUsers;
            ViewBag.TotalAdmins = totalAdmins;
            ViewBag.TotalArtists = totalArtists;
            ViewBag.TotalClients = totalClients;

            return View(masterModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleUserStatus(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.Now)
            {
                await _userManager.SetLockoutEndDateAsync(user, null);
                TempData["Success"] = "User reactivated successfully.";
            }
            else
            {
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
                Role = role,
                IsEmailConfirmed = user.EmailConfirmed
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

            var result = await _userManager.DeleteAsync(user);

            if (!result.Succeeded)
            {
                var errors = string.Join(" • ", result.Errors.Select(e => e.Description));
                TempData["Error"] = $"Could not delete user: {errors}";
                return RedirectToAction(nameof(ManageUsers));
            }

            TempData["Success"] = $"User {user.Email} deleted successfully.";
            return RedirectToAction(nameof(ManageUsers));
        }

        public async Task<IActionResult> DeletePromotedAdmins()
        {
            var users = _userManager.Users.ToList();

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);

                if (roles.Contains("Admin") && (user.Role == "Artist" || user.Role == "Client"))
                {
                    var result = await _userManager.DeleteAsync(user);

                    if (!result.Succeeded)
                    {
                        TempData["ErrorMessage"] = "An error occurred while deleting some users.";
                    }
                }
            }

            return RedirectToAction("Index", "Admin");
        }

        // ═══════════════════════════════════════════════════════════
        // RESEND CONFIRMATION EMAIL
        // ═══════════════════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendConfirmation(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction(nameof(ManageUsers));
            }

            if (user.EmailConfirmed)
            {
                TempData["Error"] = "This account is already confirmed.";
                return RedirectToAction(nameof(ManageUsers));
            }

            try
            {
                var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));

                var callbackUrl = Url.Page(
                    "/Account/ConfirmEmail",
                    pageHandler: null,
                    values: new { area = "Identity", userId = user.Id, code = code },
                    protocol: Request.Scheme,
                    host: Request.Host.Value);

                await _emailSender.SendEmailAsync(
                    user.Email,
                    "Confirm your RubiOr Account",
                    $"<h3>Welcome back!</h3><p>Please confirm your account by <a href='{callbackUrl}'>clicking here</a>.</p>");

                TempData["Success"] = $"Confirmation email resent to {user.Email}.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ResendConfirmation failed for {user.Email}: {ex.Message}");
                TempData["Error"] = "Failed to send confirmation email. Check the logs.";
            }

            return RedirectToAction(nameof(ManageUsers));
        }

        // ═══════════════════════════════════════════════════════════
        // MANAGE SERVICES - paginated
        // ═══════════════════════════════════════════════════════════
        public async Task<IActionResult> ManageServices(string search, int page = 1, int pageSize = 10)
        {
            var query = _context.Services
                .Include(s => s.ServiceCategory)
                .AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(s =>
                    s.Name.Contains(search) ||
                    (s.Description != null && s.Description.Contains(search)));
            }

            int totalCount = await query.CountAsync();
            int totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            if (totalPages < 1) totalPages = 1;
            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            var services = await query
                .OrderBy(s => s.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.Search = search;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;
            ViewBag.ShowingFrom = totalCount == 0 ? 0 : ((page - 1) * pageSize) + 1;
            ViewBag.ShowingTo = Math.Min(page * pageSize, totalCount);

            return View(services);
        }

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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteService(int id)
        {
            var service = await _context.Services.FindAsync(id);
            if (service == null) return NotFound();

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

        // ═══════════════════════════════════════════════════════════
        // ACTIVITY LOG
        // ═══════════════════════════════════════════════════════════
        private async Task LogActivity(string artistId, string message)
        {
            var log = new ActivityLog
            {
                ArtistId = artistId,
                Action = message,
                Description = $"Log generated at {DateTime.Now}",
                Timestamp = DateTime.Now
            };

            _context.ActivityLogs.Add(log);
            await _context.SaveChangesAsync();
        }

        // ═══════════════════════════════════════════════════════════
        // AUDIT LOGS - paginated
        // ═══════════════════════════════════════════════════════════
        public async Task<IActionResult> AuditLogs(int page = 1, int pageSize = 20)
        {
            var query = _context.ActivityLogs
                .Include(a => a.Artist)
                .OrderByDescending(l => l.Timestamp);

            int totalCount = await query.CountAsync();
            int totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            if (totalPages < 1) totalPages = 1;
            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            var logs = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;
            ViewBag.ShowingFrom = totalCount == 0 ? 0 : ((page - 1) * pageSize) + 1;
            ViewBag.ShowingTo = Math.Min(page * pageSize, totalCount);

            return View(logs);
        }

        // ═══════════════════════════════════════════════════════════
        // BOOKING DETAILS
        // ═══════════════════════════════════════════════════════════
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

        // ═══════════════════════════════════════════════════════════
        // MANAGE BOOKINGS - paginated + search/status filters
        // ═══════════════════════════════════════════════════════════
        public async Task<IActionResult> ManageBookings(
       string search,
       string status,
       int page = 1,
       int pageSize = 10)
        {
            var query = _context.Bookings
                .Include(b => b.Customer)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                .AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(b =>
                    b.Id.ToString().Contains(search) ||
                    (b.Customer != null && (b.Customer.FirstName + " " + b.Customer.LastName).Contains(search)) ||
                    (b.UserService != null && b.UserService.Service != null && b.UserService.Service.Name.Contains(search)));
            }

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<BookingStatus>(status, true, out var statusEnum))
            {
                query = query.Where(b => b.Status == statusEnum);
            }

            // ─── STATS (across the filtered set) ───
            int totalAll = await query.CountAsync();
            int totalPending = await query.CountAsync(b => b.Status == BookingStatus.Pending);
            int totalConfirmed = await query.CountAsync(b => b.Status == BookingStatus.Confirmed);
            int totalCompleted = await query.CountAsync(b => b.Status == BookingStatus.Completed);
            int totalCancelled = await query.CountAsync(b => b.Status == BookingStatus.Cancelled);

            var topServiceGroup = await query
                .Where(b => b.UserService != null && b.UserService.Service != null)
                .GroupBy(b => b.UserService.Service.Name)
                .Select(g => new { Name = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .FirstOrDefaultAsync();

            var topArtistGroup = await query
                .Where(b => b.UserService != null && b.UserService.Artist != null)
                .GroupBy(b => new { b.UserService.Artist.FirstName, b.UserService.Artist.LastName })
                .Select(g => new
                {
                    Name = (g.Key.FirstName + " " + g.Key.LastName).Trim(),
                    Count = g.Count()
                })
                .OrderByDescending(x => x.Count)
                .FirstOrDefaultAsync();

            int totalCount = totalAll;
            int totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            if (totalPages < 1) totalPages = 1;
            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            var bookings = await query
                .OrderByDescending(b => b.AppointmentDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.Search = search;
            ViewBag.Status = status;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;
            ViewBag.ShowingFrom = totalCount == 0 ? 0 : ((page - 1) * pageSize) + 1;
            ViewBag.ShowingTo = Math.Min(page * pageSize, totalCount);

            // ─── STATS FOR VIEW ───
            ViewBag.TotalAll = totalAll;
            ViewBag.TotalPending = totalPending;
            ViewBag.TotalConfirmed = totalConfirmed;
            ViewBag.TotalCompleted = totalCompleted;
            ViewBag.TotalCancelled = totalCancelled;
            ViewBag.TopService = topServiceGroup?.Name ?? "—";
            ViewBag.TopArtist = topArtistGroup?.Name ?? "—";

            return View(bookings);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminOverride(int bookingId, BookingStatus newStatus)
        {
            var booking = await _context.Bookings
                .Include(b => b.AvailabilitySlot)
                .Include(b => b.UserService)
                .FirstOrDefaultAsync(b => b.Id == bookingId);

            if (booking == null) return NotFound();

            booking.Status = newStatus;

            if (booking.AvailabilitySlot != null)
            {
                booking.AvailabilitySlot.IsBooked = (newStatus != BookingStatus.Cancelled &&
                                                    newStatus != BookingStatus.Rejected);
            }

            await _context.SaveChangesAsync();

            await LogActivity(booking.UserService.ArtistId, $"ADMIN OVERRIDE: Forced status to {newStatus}");

            TempData["Success"] = "Booking status successfully overridden by Admin.";
            return RedirectToAction(nameof(ManageBookings));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminReschedule(int bookingId, int newSlotId)
        {
            var booking = await _context.Bookings
                .Include(b => b.AvailabilitySlot)
                .FirstOrDefaultAsync(b => b.Id == bookingId);

            if (booking == null) return NotFound();

            var newSlot = await _context.ArtistAvailabilities
                .FirstOrDefaultAsync(a => a.Id == newSlotId && !a.IsBooked);

            if (newSlot == null)
            {
                TempData["Error"] = "The selected slot is no longer available.";
                return RedirectToAction(nameof(ManageBookings));
            }

            if (booking.AvailabilitySlotId.HasValue)
            {
                var oldSlot = await _context.ArtistAvailabilities
                    .FirstOrDefaultAsync(a => a.Id == booking.AvailabilitySlotId.Value);
                if (oldSlot != null) oldSlot.IsBooked = false;
            }

            booking.AppointmentDate = newSlot.AvailableDate.Add(newSlot.StartTime);
            booking.AvailabilitySlotId = newSlot.Id;
            newSlot.IsBooked = true;

            await _context.SaveChangesAsync();

            await LogActivity(booking.UserService.ArtistId, $"ADMIN RESCHEDULE: Moved booking to {newSlot.AvailableDate:yyyy-MM-dd} at {newSlot.StartTime:hh\\:mm}");

            TempData["Success"] = $"Booking rescheduled successfully to {newSlot.AvailableDate:MMM dd} at {newSlot.StartTime:hh\\:mm}";
            return RedirectToAction(nameof(ManageBookings));
        }

        // ═══════════════════════════════════════════════════════════
        // HERO BANNERS - paginated
        // ═══════════════════════════════════════════════════════════
        public async Task<IActionResult> HeroBanners(int page = 1, int pageSize = 10)
        {
            var query = _context.HeroBanners.AsQueryable();

            int totalCount = await query.CountAsync();
            int totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            if (totalPages < 1) totalPages = 1;
            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            var banners = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;
            ViewBag.ShowingFrom = totalCount == 0 ? 0 : ((page - 1) * pageSize) + 1;
            ViewBag.ShowingTo = Math.Min(page * pageSize, totalCount);

            return View(banners);
        }

        [HttpGet]
        public IActionResult CreateHeroBanner()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateHeroBanner(HeroBanner banner, IFormFile imageFile)
        {
            if (imageFile != null && imageFile.Length > 0)
            {
                string wwwRootPath = _hostEnvironment.WebRootPath;
                string fileName = Guid.NewGuid().ToString() + Path.GetExtension(imageFile.FileName);
                string uploadPath = Path.Combine(wwwRootPath, @"images\banners");

                if (!Directory.Exists(uploadPath)) Directory.CreateDirectory(uploadPath);

                using (var fileStream = new FileStream(Path.Combine(uploadPath, fileName), FileMode.Create))
                {
                    await imageFile.CopyToAsync(fileStream);
                }

                banner.ImagePath = "/images/banners/" + fileName;
            }

            _context.HeroBanners.Add(banner);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(HeroBanners));
        }

        [HttpGet]
        public async Task<IActionResult> EditHeroBanner(int id)
        {
            var banner = await _context.HeroBanners.FindAsync(id);
            if (banner == null) return NotFound();

            return View(banner);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditHeroBanner(HeroBanner banner, IFormFile? imageFile)
        {
            var existingBanner = await _context.HeroBanners.AsNoTracking().FirstOrDefaultAsync(b => b.Id == banner.Id);
            if (existingBanner == null) return NotFound();

            if (imageFile != null && imageFile.Length > 0)
            {
                string wwwRootPath = _hostEnvironment.WebRootPath;
                string fileName = Guid.NewGuid().ToString() + Path.GetExtension(imageFile.FileName);
                string uploadPath = Path.Combine(wwwRootPath, @"images\banners");

                using (var fileStream = new FileStream(Path.Combine(uploadPath, fileName), FileMode.Create))
                {
                    await imageFile.CopyToAsync(fileStream);
                }
                banner.ImagePath = "/images/banners/" + fileName;
            }
            else
            {
                banner.ImagePath = existingBanner.ImagePath;
            }

            _context.HeroBanners.Update(banner);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(HeroBanners));
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteHeroBanner(int id)
        {
            var banner = await _context.HeroBanners.FindAsync(id);
            if (banner == null)
            {
                TempData["Error"] = "Banner not found.";
                return RedirectToAction(nameof(HeroBanners));
            }

            if (!string.IsNullOrEmpty(banner.ImagePath))
            {
                var filePath = Path.Combine(_hostEnvironment.WebRootPath, banner.ImagePath.TrimStart('/'));
                if (System.IO.File.Exists(filePath))
                    System.IO.File.Delete(filePath);
            }

            _context.HeroBanners.Remove(banner);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Banner deleted successfully.";
            return RedirectToAction(nameof(HeroBanners));
        }
        // ═══════════════════════════════════════════════════════════
        // DISPUTES - paginated
        // ═══════════════════════════════════════════════════════════
        public async Task<IActionResult> Disputes(
            string status = null,
            string search = null,
            int page = 1,
            int pageSize = 10)
        {
            var query = _context.Bookings
                .Include(b => b.Customer)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                .Where(b => b.IsDisputed)
                .AsQueryable();

            if (!string.IsNullOrEmpty(status))
            {
                if (status == "pending")
                    query = query.Where(b => b.AdminReviewedAt == null);
                else if (status == "resolved")
                    query = query.Where(b => b.AdminReviewedAt != null);
            }

            if (!string.IsNullOrEmpty(search))
            {
                var searchLower = search.ToLower();
                query = query.Where(b =>
                    b.Id.ToString().Contains(search) ||
                    (b.Customer != null && (b.Customer.FirstName + " " + b.Customer.LastName).ToLower().Contains(searchLower)) ||
                    (b.UserService != null && b.UserService.Service != null && b.UserService.Service.Name.ToLower().Contains(searchLower))
                );
            }

            int totalCount = await query.CountAsync();
            int totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            if (totalPages < 1) totalPages = 1;
            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            var disputes = await query
                .OrderByDescending(b => b.DisputeRaisedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Stat counts on the full set (not just current page)
            var allDisputesCount = await _context.Bookings.CountAsync(b => b.IsDisputed);
            var pendingCount = await _context.Bookings.CountAsync(b => b.IsDisputed && b.AdminReviewedAt == null);
            var resolvedCount = await _context.Bookings.CountAsync(b => b.IsDisputed && b.AdminReviewedAt != null);

            ViewBag.Total = allDisputesCount;
            ViewBag.Pending = pendingCount;
            ViewBag.Resolved = resolvedCount;
            ViewBag.SelectedStatus = status;
            ViewBag.SearchQuery = search;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;
            ViewBag.ShowingFrom = totalCount == 0 ? 0 : ((page - 1) * pageSize) + 1;
            ViewBag.ShowingTo = Math.Min(page * pageSize, totalCount);

            return View(disputes);
        }

        public async Task<IActionResult> DisputeDetail(int id)
        {
            var booking = await _context.Bookings
                .Include(b => b.Customer)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                    .ThenInclude(a => a.ArtistProfile)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null) return NotFound();

            if (!booking.IsDisputed)
            {
                TempData["Error"] = "This booking is not under dispute.";
                return RedirectToAction("Disputes");
            }

            return View(booking);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResolveDispute(int id, string resolution, decimal amount = 0, string adminNotes = null)
        {
            var booking = await _context.Bookings
                .Include(b => b.Customer)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null) return NotFound();

            if (!booking.IsDisputed)
            {
                TempData["Error"] = "This booking is not under dispute.";
                return RedirectToAction("Disputes");
            }

            if (booking.AdminReviewedAt != null)
            {
                TempData["Error"] = "This dispute has already been resolved.";
                return RedirectToAction("Disputes");
            }

            decimal totalPaid = booking.DepositPaid + booking.FinalPaymentPaid;

            if (resolution == "partial_split" && amount <= 0)
            {
                TempData["Error"] = "Please enter a valid amount for partial split.";
                return RedirectToAction("DisputeDetail", new { id });
            }

            if (resolution == "partial_split" && amount > totalPaid)
            {
                TempData["Error"] = $"Amount cannot exceed total paid (R{totalPaid:N2}).";
                return RedirectToAction("DisputeDetail", new { id });
            }

            booking.AdminReviewedAt = DateTime.UtcNow;
            booking.AdminResolution = resolution;
            booking.AdminResolutionAmount = amount;
            booking.AdminNotes = adminNotes;
            booking.Status = BookingStatus.Resolved;

            await _context.SaveChangesAsync();

            switch (resolution)
            {
                case "release_to_artist":
                    await ReleaseFundsToArtist(booking);
                    break;
                case "refund_to_client":
                    await RefundFundsToClient(booking);
                    break;
                case "partial_split":
                    await PartialSplitFunds(booking, amount);
                    break;
                default:
                    TempData["Error"] = "Invalid resolution selected.";
                    return RedirectToAction("DisputeDetail", new { id });
            }

            await SendResolutionEmails(booking, resolution, amount);

            TempData["Success"] = $"Dispute #{booking.Id} resolved successfully.";
            return RedirectToAction("Disputes");
        }

        // ─── DISPUTE HELPERS ───
        private async Task ReleaseFundsToArtist(Booking booking)
        {
            try
            {
                var artistProfile = await _context.ArtistProfiles
                    .FirstOrDefaultAsync(p => p.UserId == booking.UserService.ArtistId);

                if (artistProfile == null || string.IsNullOrEmpty(artistProfile.RecipientCode))
                {
                    Console.WriteLine($"⚠️ No recipient code for artist {booking.UserService.ArtistId}");
                    return;
                }

                decimal artistNetPayout = booking.ServicePrice - booking.PlatformCommission;

                if (artistNetPayout <= 0)
                {
                    Console.WriteLine($"⚠️ Artist payout is zero or negative for booking {booking.Id}");
                    return;
                }

                int amountInCents = (int)(artistNetPayout * 100);
                string reference = $"DISPUTE_RELEASE_{booking.Id}_{DateTime.UtcNow:yyyyMMddHHmmss}";

                var result = await _paystackService.InitiateTransferAsync(
                    recipientCode: artistProfile.RecipientCode,
                    amountInCents: amountInCents,
                    reference: reference,
                    reason: $"Dispute resolved in artist's favour — booking #{booking.Id}"
                );

                if (result.Success)
                {
                    booking.ArtistTotalEarned = artistNetPayout;
                    booking.FundsReleasedAt = DateTime.UtcNow;
                    booking.IsFundsReleased = true;
                    await _context.SaveChangesAsync();

                    Console.WriteLine($"✅ Dispute payout sent: {result.TransferCode} — R{artistNetPayout} to artist {booking.UserService.ArtistId}");
                }
                else
                {
                    Console.WriteLine($"❌ Dispute payout failed: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ReleaseFundsToArtist (admin) error: {ex.Message}");
            }
        }

        private async Task RefundFundsToClient(Booking booking)
        {
            try
            {
                decimal totalPaid = booking.DepositPaid + booking.FinalPaymentPaid;

                if (totalPaid <= 0)
                {
                    Console.WriteLine($"⚠️ No payment found for booking {booking.Id}");
                    return;
                }

                booking.RefundAmount = totalPaid;
                booking.RefundDate = DateTime.UtcNow;
                booking.IsRefunded = true;
                booking.DepositPaid = 0m;
                booking.FinalPaymentPaid = 0m;
                booking.IsDepositPaid = false;

                await _context.SaveChangesAsync();

                Console.WriteLine($"✅ Refunded R{totalPaid} to client for booking {booking.Id}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ RefundFundsToClient error: {ex.Message}");
                throw;
            }
        }

        private async Task PartialSplitFunds(Booking booking, decimal refundAmount)
        {
            try
            {
                decimal totalPaid = booking.DepositPaid + booking.FinalPaymentPaid;
                decimal artistAmount = totalPaid - refundAmount;

                if (refundAmount > 0)
                {
                    booking.RefundAmount = refundAmount;
                    booking.RefundDate = DateTime.UtcNow;
                    booking.IsRefunded = true;
                }

                if (artistAmount > 0)
                {
                    var artistProfile = await _context.ArtistProfiles
                        .FirstOrDefaultAsync(p => p.UserId == booking.UserService.ArtistId);

                    if (artistProfile == null || string.IsNullOrEmpty(artistProfile.RecipientCode))
                    {
                        Console.WriteLine($"⚠️ No recipient code for artist {booking.UserService.ArtistId}");
                    }
                    else
                    {
                        int amountInCents = (int)(artistAmount * 100);
                        string reference = $"DISPUTE_SPLIT_{booking.Id}_{DateTime.UtcNow:yyyyMMddHHmmss}";

                        var result = await _paystackService.InitiateTransferAsync(
                            recipientCode: artistProfile.RecipientCode,
                            amountInCents: amountInCents,
                            reference: reference,
                            reason: $"Partial split payout for booking #{booking.Id}"
                        );

                        if (result.Success)
                        {
                            booking.ArtistTotalEarned = artistAmount;
                            booking.FundsReleasedAt = DateTime.UtcNow;
                            booking.IsFundsReleased = true;
                            Console.WriteLine($"✅ Partial split payout sent: {result.TransferCode} — R{artistAmount} to artist");
                        }
                        else
                        {
                            Console.WriteLine($"❌ Partial split payout failed: {result.Message}");
                        }
                    }
                }

                booking.AdminResolutionAmount = refundAmount;
                booking.DepositPaid = 0m;
                booking.FinalPaymentPaid = 0m;
                booking.IsDepositPaid = false;

                await _context.SaveChangesAsync();

                Console.WriteLine($"✅ Partial split: R{refundAmount} to client, R{artistAmount} to artist");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ PartialSplitFunds error: {ex.Message}");
            }
        }

        private async Task SendResolutionEmails(Booking booking, string resolution, decimal amount)
        {
            try
            {
                string resolutionText = resolution switch
                {
                    "release_to_artist" => "The dispute was resolved in the artist's favour. Funds have been released to the artist.",
                    "refund_to_client" => $"The dispute was resolved in your favour. A refund of R{amount:N2} has been processed.",
                    "partial_split" => $"A partial refund of R{amount:N2} has been processed. The remaining amount has been released to the artist.",
                    _ => "The dispute has been resolved."
                };

                if (booking.Customer != null && !string.IsNullOrEmpty(booking.Customer.Email))
                {
                    string clientSubject = "Dispute Resolved";
                    string clientBody = $@"
            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #f0c808; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
                <h2 style='color: #f0c808;'>Dispute Resolved</h2>
                <p>Dear {booking.Customer.FirstName},</p>
                <p>Your dispute for <strong>{booking.UserService?.Service?.Name}</strong> has been resolved.</p>
                <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
                    <p><strong>Booking ID:</strong> #{booking.Id}</p>
                    <p><strong>Service:</strong> {booking.UserService?.Service?.Name}</p>
                    <p><strong>Resolution:</strong> {resolutionText}</p>
                </div>
                <hr style='border-color: #333;'>
                <p style='font-size: 12px; color: #666;'>RubiOr</p>
            </div>";

                    await _commService.SendDirectMessageEmailAsync(booking.UserService.ArtistId, booking.CustomerId, clientSubject, clientBody);
                }

                if (booking.UserService?.Artist != null && !string.IsNullOrEmpty(booking.UserService.Artist.Email))
                {
                    string artistSubject = "Dispute Resolved";
                    string artistBody = $@"
            <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #f0c808; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
                <h2 style='color: #f0c808;'>Dispute Resolved</h2>
                <p>Dear {booking.UserService.Artist.FirstName},</p>
                <p>The dispute for <strong>{booking.UserService?.Service?.Name}</strong> has been resolved.</p>
                <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
                    <p><strong>Booking ID:</strong> #{booking.Id}</p>
                    <p><strong>Client:</strong> {booking.Customer?.FirstName}</p>
                    <p><strong>Resolution:</strong> {resolutionText}</p>
                </div>
                <hr style='border-color: #333;'>
                <p style='font-size: 12px; color: #666;'>RubiOr</p>
            </div>";

                    await _commService.SendDirectMessageEmailAsync(booking.CustomerId, booking.UserService.ArtistId, artistSubject, artistBody);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ SendResolutionEmails error: {ex.Message}");
            }
        }
    }
}