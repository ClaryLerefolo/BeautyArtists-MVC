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

namespace BeautyArtists.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _hostEnvironment;
        private readonly ICommunicationService _commService;

        // ─── ✅ NEW PRICING CONSTANTS ───
        private const decimal CLIENT_MARKUP_RATE = 0.04m;      // 4% markup for client
        private const decimal BOOKING_FEE = 5.00m;
        private const decimal NEW_CLIENT_COMMISSION = 0.10m;   // 10% for new clients
        private const decimal REPEAT_CLIENT_FLAT_FEE = 15.00m; // R15 for repeat clients
        private const decimal MIN_PLATFORM_FEE = 8.00m;        // Safeguard: min R8

        public AdminController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IWebHostEnvironment hostEnvironment, ICommunicationService commService)
        {
            _context = context;
            _userManager = userManager;
            _hostEnvironment = hostEnvironment;
        }

        // ─── HELPER: Check if client is new ───
        private async Task<bool> IsNewClient(string customerId, string artistId)
        {
            var existingBookings = await _context.Bookings
                .Where(b => b.CustomerId == customerId
                            && b.UserService.ArtistId == artistId
                            && b.Status != BookingStatus.Cancelled
                            && b.Status != BookingStatus.Rejected)
                .AnyAsync();

            return !existingBookings;
        }

        // ─── HELPER: Calculate platform fee ───
        private decimal GetPlatformFee(decimal servicePrice, bool isNewClient)
        {
            var platformFee = isNewClient
                ? servicePrice * NEW_CLIENT_COMMISSION
                : REPEAT_CLIENT_FLAT_FEE;

            return Math.Max(platformFee, MIN_PLATFORM_FEE);
        }

        // ─── HELPER: Calculate artist payout ───
        private decimal GetArtistPayout(decimal servicePrice, bool isNewClient)
        {
            return servicePrice - GetPlatformFee(servicePrice, isNewClient);
        }

        // ─── HELPER: Calculate client total ───
        private decimal GetClientTotal(decimal servicePrice)
        {
            return (servicePrice * (1 + CLIENT_MARKUP_RATE)) + BOOKING_FEE;
        }

        public async Task<IActionResult> Index()
        {
            var model = new AdminDashboardViewModel
            {
                TotalUsers = await _userManager.Users.CountAsync(),
                TotalArtists = await _userManager.GetUsersInRoleAsync("Artist").ContinueWith(t => t.Result.Count),
                TotalCustomers = await _userManager.GetUsersInRoleAsync("Client").ContinueWith(t => t.Result.Count),
                TotalBookings = await _context.Bookings.CountAsync(),

                // ─── ✅ FIXED: CORRECT PLATFORM EARNINGS ───
                TotalRevenue = await CalculateTotalPlatformEarnings(),
                RevenuePerArtist = await CalculateRevenuePerArtist()
            };

            return View("Index", model);
        }

        // ─── ✅ NEW: Calculate total platform earnings ───
        private async Task<decimal> CalculateTotalPlatformEarnings()
        {
            var completedBookings = await _context.Bookings
                .Include(b => b.UserService)
                .Where(b => b.Status == BookingStatus.Completed)
                .ToListAsync();

            decimal total = 0m;
            foreach (var booking in completedBookings)
            {
                bool isNew = await IsNewClient(booking.CustomerId, booking.UserService.ArtistId);
                decimal platformFee = GetPlatformFee(booking.ServicePrice, isNew);
                decimal markup = booking.ServicePrice * CLIENT_MARKUP_RATE;
                total += markup + platformFee + booking.BookingFee;
            }

            return total;
        }

        // ─── ✅ NEW: Calculate revenue per artist ───
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

                bool isNew = await IsNewClient(booking.CustomerId, artistId);
                decimal platformFee = GetPlatformFee(booking.ServicePrice, isNew);
                decimal markup = booking.ServicePrice * CLIENT_MARKUP_RATE;
                result[artistId].TotalRevenue += markup + platformFee + booking.BookingFee;
            }

            return result.Values.ToList();
        }

        public async Task<IActionResult> ManageUsers(string search)
        {
            var users = await _userManager.Users.ToListAsync();
            var userList = new List<UserManagementViewModel>();
            foreach (var user in users)
            {
                var role = (await _userManager.GetRolesAsync(user)).FirstOrDefault() ?? "None";

                bool isDeactivated = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.Now;

                userList.Add(new UserManagementViewModel
                {
                    Id = user.Id,
                    FullName = $"{user.FirstName} {user.LastName}",
                    Email = user.Email,
                    Role = role,
                    IsDeactivated = isDeactivated
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

        public async Task<IActionResult> ManageServices()
        {
            var services = await _context.Services
                .Include(s => s.ServiceCategory)
                .OrderBy(s => s.Name)
                .ToListAsync();
            return View(services);
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

        public async Task<IActionResult> AuditLogs()
        {
            var logs = await _context.ActivityLogs
                .Include(a => a.Artist)
                .OrderByDescending(l => l.Timestamp)
                .ToListAsync();

            return View(logs);
        }

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

        public async Task<IActionResult> ManageBookings()
        {
            var allBookings = await _context.Bookings
                .Include(b => b.Customer)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                .OrderByDescending(b => b.AppointmentDate)
                .ToListAsync();

            return View(allBookings);
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

        // ─── ✅ NEW: ADMIN RESCHEDULE - NO 24-HOUR RESTRICTION ───
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

            // ─── ✅ NO 24-HOUR RESTRICTION FOR ADMIN ───
            // Release old slot
            if (booking.AvailabilitySlotId.HasValue)
            {
                var oldSlot = await _context.ArtistAvailabilities
                    .FirstOrDefaultAsync(a => a.Id == booking.AvailabilitySlotId.Value);
                if (oldSlot != null) oldSlot.IsBooked = false;
            }

            // Assign new slot
            booking.AppointmentDate = newSlot.AvailableDate.Add(newSlot.StartTime);
            booking.AvailabilitySlotId = newSlot.Id;
            newSlot.IsBooked = true;

            await _context.SaveChangesAsync();

            await LogActivity(booking.UserService.ArtistId, $"ADMIN RESCHEDULE: Moved booking to {newSlot.AvailableDate:yyyy-MM-dd} at {newSlot.StartTime:hh\\:mm}");

            TempData["Success"] = $"Booking rescheduled successfully to {newSlot.AvailableDate:MMM dd} at {newSlot.StartTime:hh\\:mm}";
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
        // ══════════════════════════════════
        //  DISPUTES - List all disputes
        // ══════════════════════════════════
        public async Task<IActionResult> Disputes(string status = null, string search = null)
        {
            var query = _context.Bookings
                .Include(b => b.Customer)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                .Where(b => b.IsDisputed);

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

            var disputes = await query
                .OrderByDescending(b => b.DisputeRaisedAt)
                .ToListAsync();

            ViewBag.Total = disputes.Count;
            ViewBag.Pending = disputes.Count(b => b.AdminReviewedAt == null);
            ViewBag.Resolved = disputes.Count(b => b.AdminReviewedAt != null);
            ViewBag.SelectedStatus = status;
            ViewBag.SearchQuery = search;

            return View(disputes);
        }

        // ══════════════════════════════════
        //  DISPUTE DETAIL - View specific dispute
        // ══════════════════════════════════
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

        // ══════════════════════════════════
        //  RESOLVE DISPUTE - Apply resolution
        // ══════════════════════════════════
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

        // ══════════════════════════════════
        //  HELPER: Release funds to artist
        // ══════════════════════════════════
        private async Task ReleaseFundsToArtist(Booking booking)
        {
            try
            {
                var artistProfile = await _context.ArtistProfiles
                    .FirstOrDefaultAsync(p => p.UserId == booking.UserService.ArtistId);

                if (artistProfile == null || string.IsNullOrEmpty(artistProfile.SubaccountCode))
                {
                    Console.WriteLine($"⚠️ No subaccount for artist {booking.UserService.ArtistId}");
                    return;
                }

                decimal totalPaid = booking.DepositPaid + booking.FinalPaymentPaid;

                if (totalPaid <= 0)
                {
                    Console.WriteLine($"⚠️ No payment found for booking {booking.Id}");
                    return;
                }

                // ─── TRANSFER TO ARTIST SUBACCOUNT ───
                // await _paymentService.TransferToSubaccount(artistProfile.SubaccountCode, totalPaid);

                booking.ArtistTotalEarned = totalPaid;
                booking.FundsReleasedAt = DateTime.UtcNow;
                booking.IsFundsReleased = true;

                await _context.SaveChangesAsync();

                Console.WriteLine($"✅ Released R{totalPaid} to artist {booking.UserService.ArtistId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ReleaseFundsToArtist error: {ex.Message}");
                throw;
            }
        }

        // ══════════════════════════════════
        //  HELPER: Refund funds to client
        // ══════════════════════════════════
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

                // ─── PROCESS REFUND ───
                // await _paymentService.RefundPayment(booking.Id, totalPaid);

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

        // ══════════════════════════════════
        //  HELPER: Partial split funds
        // ══════════════════════════════════
        private async Task PartialSplitFunds(Booking booking, decimal refundAmount)
        {
            try
            {
                decimal totalPaid = booking.DepositPaid + booking.FinalPaymentPaid;
                decimal artistAmount = totalPaid - refundAmount;

                if (refundAmount > 0)
                {
                    // await _paymentService.RefundPayment(booking.Id, refundAmount);
                    booking.RefundAmount = refundAmount;
                    booking.RefundDate = DateTime.UtcNow;
                    booking.IsRefunded = true;
                }

                if (artistAmount > 0)
                {
                    var artistProfile = await _context.ArtistProfiles
                        .FirstOrDefaultAsync(p => p.UserId == booking.UserService.ArtistId);

                    if (artistProfile != null && !string.IsNullOrEmpty(artistProfile.SubaccountCode))
                    {
                        // await _paymentService.TransferToSubaccount(artistProfile.SubaccountCode, artistAmount);
                        booking.ArtistTotalEarned = artistAmount;
                        booking.FundsReleasedAt = DateTime.UtcNow;
                        booking.IsFundsReleased = true;
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
                throw;
            }
        }

        // ══════════════════════════════════
        //  HELPER: Send resolution EMAILS only (NO NOTIFICATIONS)
        // ══════════════════════════════════
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

                // ─── EMAIL TO CLIENT ───
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

                // ─── EMAIL TO ARTIST ───
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