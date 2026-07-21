using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Models.ViewModels;
using BeautyArtists.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Mail;
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
        private readonly ICommunicationService _commService;
        private readonly INotificationService _notificationService;
        private readonly IPaystackService _paystackService;
        private readonly IConfiguration _configuration;

        public ArtistController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment env,
            ICommunicationService communicationService,
            INotificationService notificationService,
            IPaystackService paystackService,
            IConfiguration configuration)
        {
            _context = context;
            _userManager = userManager;
            _env = env;
            _commService = communicationService;
            _notificationService = notificationService;
            _paystackService = paystackService;
            _configuration = configuration;
        }

        // ═══════════════════════════════════════════════════════════
        // DASHBOARD
        // ═══════════════════════════════════════════════════════════
        public async Task<IActionResult> Dashboard()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var artistId = user.Id;

            // ─── GET COUNTS (SEQUENTIALLY) ───
            var portfolioCount = await _context.PortfolioItems
                .Where(p => p.ArtistId == artistId)
                .AsNoTracking()
                .CountAsync();

            var servicesCount = await _context.UserServices
                .Where(us => us.ArtistId == artistId)
                .AsNoTracking()
                .CountAsync();

            var upcomingCount = await _context.Bookings
                .Where(b => b.UserService.ArtistId == artistId
                            && b.AppointmentDate > DateTime.Now
                            && b.Status == BookingStatus.Confirmed)
                .AsNoTracking()
                .CountAsync();

            // ─── MONTHLY EARNINGS CHART ───
            var sixMonthsAgo = DateTime.Now.AddMonths(-5);
            var monthlyEarnings = await _context.Bookings
                .Where(b => b.UserService.ArtistId == artistId
                            && b.Status == BookingStatus.Confirmed
                            && b.AppointmentDate >= sixMonthsAgo)
                .GroupBy(b => new { b.AppointmentDate.Year, b.AppointmentDate.Month })
                .Select(g => new
                {
                    Month = g.Key.Month,
                    Year = g.Key.Year,
                    Total = g.Sum(b => (decimal?)b.UserService.Price) ?? 0
                })
                .ToDictionaryAsync(k => $"{k.Year}-{k.Month:D2}", v => v.Total);

            // ─── RECENT APPOINTMENTS ───
            var recentAppointments = await _context.Bookings
                .Where(b => b.UserService.ArtistId == artistId)
                .OrderByDescending(b => b.AppointmentDate)
                .Take(4)
                .Select(b => new AppointmentSummary
                {
                    ClientName = b.Customer != null
                        ? (!string.IsNullOrEmpty(b.Customer.FirstName)
                            ? $"{b.Customer.FirstName} {b.Customer.LastName}"
                            : b.Customer.Email ?? "Unknown Client")
                        : "Unknown Client",
                    ServiceName = b.UserService != null && b.UserService.Service != null
                        ? b.UserService.Service.Name
                        : "Unknown Service",
                    AppointmentDate = b.AppointmentDate,
                    Status = b.Status.ToString()
                })
                .ToListAsync();

            // ─── BUILD CHART DATA ───
            var chartData = new List<decimal>();
            var chartLabels = new List<string>();
            for (int i = 5; i >= 0; i--)
            {
                var monthDate = DateTime.Now.AddMonths(-i);
                var key = $"{monthDate.Year}-{monthDate.Month:D2}";
                chartData.Add(monthlyEarnings.GetValueOrDefault(key, 0));
                chartLabels.Add(monthDate.ToString("MMM"));
            }

            var model = new ArtistDashboardViewModel
            {
                ArtistName = user.FullName ?? $"{user.FirstName} {user.LastName}".Trim() ?? user.Email ?? "Artist",
                PortfolioItemsCount = portfolioCount,
                ServicesCount = servicesCount,
                UpcomingAppointments = upcomingCount,
                MonthlyEarnings = chartData.Sum(),
                RecentAppointments = recentAppointments,
                MonthlyEarningsGraph = chartData,
                MonthlyLabels = chartLabels
            };

            return View(model);
        }

        // ═══════════════════════════════════════════════════════════
        // PROFILE
        // ═══════════════════════════════════════════════════════════
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
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var profile = await _context.ArtistProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
            if (profile == null) return NotFound();

            // Update basic fields
            profile.FullName = updatedProfile.FullName ?? profile.FullName;
            profile.Bio = updatedProfile.Bio ?? profile.Bio;
            profile.YearsExperience = updatedProfile.YearsExperience;
            profile.Province = updatedProfile.Province ?? profile.Province;
            profile.City = updatedProfile.City ?? profile.City;
            profile.ContactInfo = updatedProfile.ContactInfo ?? profile.ContactInfo;
            profile.InstagramUrl = updatedProfile.InstagramUrl ?? profile.InstagramUrl;
            profile.FacebookUrl = updatedProfile.FacebookUrl ?? profile.FacebookUrl;
            profile.TwitterUrl = updatedProfile.TwitterUrl ?? profile.TwitterUrl;
            profile.TikTokUrl = updatedProfile.TikTokUrl ?? profile.TikTokUrl;

            // ── NEW: Studio address fields (for walk‑in) ──
            profile.StudioAddress = updatedProfile.StudioAddress ?? profile.StudioAddress;
            profile.StudioCity = updatedProfile.StudioCity ?? profile.StudioCity;
            profile.StudioProvince = updatedProfile.StudioProvince ?? profile.StudioProvince;
            profile.StudioPostalCode = updatedProfile.StudioPostalCode ?? profile.StudioPostalCode;
            profile.StudioLatitude = updatedProfile.StudioLatitude ?? profile.StudioLatitude;
            profile.StudioLongitude = updatedProfile.StudioLongitude ?? profile.StudioLongitude;

            // Handle profile picture
            if (ProfilePicture != null && ProfilePicture.Length > 0)
            {
                var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads/profile_pictures");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                if (!string.IsNullOrEmpty(profile.ProfilePictureUrl) && !profile.ProfilePictureUrl.StartsWith("/images/"))
                {
                    var oldPath = Path.Combine(_env.WebRootPath, profile.ProfilePictureUrl.TrimStart('/'));
                    if (System.IO.File.Exists(oldPath))
                        System.IO.File.Delete(oldPath);
                }

                var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(ProfilePicture.FileName);
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);
                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await ProfilePicture.CopyToAsync(fileStream);
                }
                profile.ProfilePictureUrl = "/uploads/profile_pictures/" + uniqueFileName;
            }

            _context.Entry(profile).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            TempData["Success"] = "Profile updated successfully!";
            return RedirectToAction(nameof(Profile));
        }

        [HttpPost]
        public async Task<IActionResult> UpdatePicture(IFormFile ProfilePicture)
        {
            var user = await _userManager.GetUserAsync(User);
            var profile = await _context.ArtistProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
            if (profile == null || ProfilePicture == null || ProfilePicture.Length == 0)
                return RedirectToAction(nameof(Profile));

            var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads/profile_pictures");
            if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

            if (!string.IsNullOrEmpty(profile.ProfilePictureUrl) && !profile.ProfilePictureUrl.StartsWith("/images/"))
            {
                var oldPath = Path.Combine(_env.WebRootPath, profile.ProfilePictureUrl.TrimStart('/'));
                if (System.IO.File.Exists(oldPath))
                    System.IO.File.Delete(oldPath);
            }

            var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(ProfilePicture.FileName);
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
                await ProfilePicture.CopyToAsync(stream);

            profile.ProfilePictureUrl = "/uploads/profile_pictures/" + uniqueFileName;
            _context.ArtistProfiles.Update(profile);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Profile));
        }

        // ═══════════════════════════════════════════════════════════
        // ARTIST SERVICE DETAIL
        // ═══════════════════════════════════════════════════════════
        public async Task<IActionResult> ArtistServiceDetail(int id)
        {
            var userId = _userManager.GetUserId(User);
            var userService = await _context.UserServices
                .Include(us => us.Service)
                    .ThenInclude(s => s.ServiceCategory)
                .Include(us => us.Artist)
                .FirstOrDefaultAsync(us => us.Id == id && us.ArtistId == userId);
            if (userService == null) return NotFound();

            var portfolioItems = await _context.PortfolioItems
                .Where(p => p.UserServiceId == id && p.ArtistId == userId)
                .OrderBy(p => p.DisplayOrder)
                .ToListAsync();

            var defaultPortfolio = await _context.Portfolios
                .FirstOrDefaultAsync(p => p.ArtistId == userId);
            var defaultPortfolioId = defaultPortfolio?.Id ?? 0;
            if (defaultPortfolio == null)
            {
                defaultPortfolio = new Portfolio
                {
                    Name = "General Portfolio",
                    Description = "Auto-created portfolio.",
                    ArtistId = userId,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Portfolios.Add(defaultPortfolio);
                await _context.SaveChangesAsync();
                defaultPortfolioId = defaultPortfolio.Id;
            }

            var vm = new ArtistServiceDetailViewModel
            {
                UserService = userService,
                PortfolioItems = portfolioItems,
                DefaultPortfolioId = defaultPortfolioId
            };

            ViewBag.Categories = await _context.ServiceCategories
                .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name })
                .ToListAsync();

            return View(vm);
        }

        // ═══════════════════════════════════════════════════════════
        // MANAGE SERVICES
        // ═══════════════════════════════════════════════════════════
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

            ViewBag.RawServices = available.Select(s => new
            {
                id = s.Id,
                name = s.Name,
                serviceCategory = new { name = s.ServiceCategory?.Name ?? "General" },
                basePrice = s.BasePrice,
                duration = s.Duration,
                description = s.Description
            }).ToList();

            var portfolioItems = await _context.PortfolioItems
                .Where(p => p.ArtistId == userId)
                .ToListAsync();

            var portfolioItemsByService = portfolioItems
                .Where(p => p.UserServiceId.HasValue)
                .GroupBy(p => p.UserServiceId.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            ViewBag.PortfolioItemsByService = portfolioItemsByService;

            var defaultPortfolio = await _context.Portfolios
                .FirstOrDefaultAsync(p => p.ArtistId == userId);
            if (defaultPortfolio == null)
            {
                defaultPortfolio = new Portfolio
                {
                    Name = "General Portfolio",
                    Description = "Auto-created portfolio for service items.",
                    ArtistId = userId,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Portfolios.Add(defaultPortfolio);
                await _context.SaveChangesAsync();
            }
            ViewBag.DefaultPortfolioId = defaultPortfolio.Id;

            ViewBag.Categories = await _context.ServiceCategories
                .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name })
                .ToListAsync();

            return View(userServices);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddService(UserService model, IFormFile? ImageFile)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId)) return Challenge();

            var baseService = await _context.Services.FindAsync(model.ServiceId);
            if (baseService == null)
            {
                TempData["Error"] = "Invalid Service Selection. Please try again.";
                return RedirectToAction(nameof(ManageServices));
            }

            model.ArtistId = userId;
            model.IsActive = true;

            ModelState.Clear();

            if (model.Price <= 0 || model.Duration <= 0)
            {
                TempData["Error"] = "Price and Duration must be greater than zero.";
                return RedirectToAction(nameof(ManageServices));
            }

            try
            {
                if (ImageFile != null && ImageFile.Length > 0)
                {
                    var uploadDir = Path.Combine(_env.WebRootPath, "uploads/services");
                    if (!Directory.Exists(uploadDir)) Directory.CreateDirectory(uploadDir);

                    var fileName = $"{Guid.NewGuid()}_{Path.GetFileName(ImageFile.FileName)}";
                    var filePath = Path.Combine(uploadDir, fileName);
                    using (var stream = new FileStream(filePath, FileMode.Create))
                        await ImageFile.CopyToAsync(stream);
                    model.ImagePath = "/uploads/services/" + fileName;
                }

                _context.UserServices.Add(model);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Service added successfully!";
                return RedirectToAction(nameof(ManageServices));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ AddService ERROR: {ex.Message}");
                TempData["Error"] = $"Failed to add service: {ex.Message}";
                return RedirectToAction(nameof(ManageServices));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditUserService(UserService model, IFormFile? ImageFile)
        {
            var existingService = await _context.UserServices.FindAsync(model.Id);
            if (existingService == null) return NotFound();

            ModelState.Remove("ArtistId");
            ModelState.Remove("ServiceId");
            ModelState.Remove("Artist");
            ModelState.Remove("Service");
            ModelState.Remove("ImagePath");

            var isActiveForm = Request.Form["IsActive"].FirstOrDefault();
            model.IsActive = !string.IsNullOrEmpty(isActiveForm) && isActiveForm == "true";

            existingService.Price = model.Price;
            existingService.Duration = model.Duration;
            existingService.CustomDescription = model.CustomDescription;
            existingService.IsActive = model.IsActive;

            if (ImageFile != null && ImageFile.Length > 0)
            {
                if (!string.IsNullOrEmpty(existingService.ImagePath))
                {
                    var oldPath = Path.Combine(_env.WebRootPath, existingService.ImagePath.TrimStart('/'));
                    if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
                }

                var uploadDir = Path.Combine(_env.WebRootPath, "uploads/services");
                if (!Directory.Exists(uploadDir)) Directory.CreateDirectory(uploadDir);
                var fileName = $"{Guid.NewGuid()}_{Path.GetFileName(ImageFile.FileName)}";
                var filePath = Path.Combine(uploadDir, fileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await ImageFile.CopyToAsync(stream);
                }
                existingService.ImagePath = "/uploads/services/" + fileName;
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = "Service updated successfully!";
            return RedirectToAction(nameof(ManageServices));
        }

        [HttpPost]
        public async Task<IActionResult> DeleteUserService(int id)
        {
            var userService = await _context.UserServices.FindAsync(id);
            if (userService == null) return NotFound();

            try
            {
                if (!string.IsNullOrEmpty(userService.ImagePath))
                {
                    var filePath = Path.Combine(_env.WebRootPath, userService.ImagePath.TrimStart('/'));
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }
                }

                _context.UserServices.Remove(userService);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception)
            {
                return BadRequest("Could not delete service.");
            }
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

        // ═══════════════════════════════════════════════════════════
        // MY APPOINTMENTS
        // ═══════════════════════════════════════════════════════════
        public async Task<IActionResult> MyAppointments(
        int page = 1,
        int pageSize = 10,
        string status = null,
        DateTime? fromDate = null,
        DateTime? toDate = null)
        {
            var artistId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(artistId)) return Challenge();

            var query = _context.Bookings
                .Include(b => b.Customer)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService.Artist)
                .Where(b => b.UserService.ArtistId == artistId);

            // ─── FILTERS ───
            if (!string.IsNullOrEmpty(status))
            {
                if (Enum.TryParse<BookingStatus>(status, true, out var statusEnum))
                    query = query.Where(b => b.Status == statusEnum);
            }

            if (fromDate.HasValue)
                query = query.Where(b => b.AppointmentDate >= fromDate.Value);

            if (toDate.HasValue)
                query = query.Where(b => b.AppointmentDate <= toDate.Value);

            var totalCount = await query.CountAsync();

            var bookings = await query
                .OrderByDescending(b => b.AppointmentDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            ViewBag.TotalCount = totalCount;
            ViewBag.SelectedStatus = status;
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

            return View(bookings);
        }

        // ═══════════════════════════════════════════════════════════
        // UPDATE TRANSPORT COST
        // ═══════════════════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateTransportCost(int bookingId, decimal transportCost)
        {
            var artistId = _userManager.GetUserId(User);

            var booking = await _context.Bookings
                .Include(b => b.UserService)
                .Include(b => b.Customer)
                .FirstOrDefaultAsync(b => b.Id == bookingId && b.UserService.ArtistId == artistId);

            if (booking == null)
                return NotFound();

            if (booking.SelectedLocationType != LocationType.HouseCall)
            {
                TempData["Error"] = "Transport costs only apply to House Call bookings.";
                return RedirectToAction("MyAppointments");
            }

            if (booking.Status != BookingStatus.Pending && booking.Status != BookingStatus.Confirmed)
            {
                TempData["Error"] = "Transport cost can only be set for pending or confirmed bookings.";
                return RedirectToAction("MyAppointments");
            }

            booking.TransportCost = transportCost;
            booking.TotalAmount = (booking.UserService?.Price ?? 0) + transportCost;

            await _context.SaveChangesAsync();

            if (booking.Customer != null && !string.IsNullOrEmpty(booking.Customer.Email))
            {
                string subject = "🚗 Transport Cost Added to Your Booking";
                string body = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px;'>
                    <h2 style='color: #f0c808;'>Transport Cost Update</h2>
                    <p>Dear {booking.Customer.FirstName},</p>
                    <p>The artist has added <strong>R{transportCost:N2}</strong> for transport to your location.</p>
                    <p><strong>New Total Amount:</strong> R{booking.TotalAmount:N2}</p>
                    <p>The artist will review and confirm your booking shortly.</p>
                </div>";

                await _commService.SendDirectMessageEmailAsync(artistId, booking.CustomerId, subject, body);
            }

            TempData["Success"] = $"Transport cost of R{transportCost:N2} added successfully!";
            return RedirectToAction("MyAppointments");
        }

        // ═══════════════════════════════════════════════════════════
        // BANKING
        // ═══════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> Banking()
        {
            var user = await _userManager.GetUserAsync(User);
            var profile = await _context.ArtistProfiles
                .FirstOrDefaultAsync(p => p.UserId == user.Id);

            if (profile == null) return NotFound();

            var banks = GetTestBanks();

            var model = new BankingViewModel
            {
                BankName = profile.BankName ?? "",
                BankCode = profile.BankCode ?? "",
                AccountHolderName = profile.AccountHolderName ?? "",
                IsBankAccountVerified = profile.IsBankAccountVerified,
                SubaccountCode = profile.SubaccountCode ?? "",
                Banks = banks.Select(b => new SelectListItem
                {
                    Value = b.Code,
                    Text = b.Name
                }).ToList()
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Banking(BankingViewModel model)
        {
            var banks = GetTestBanks();
            model.Banks = banks.Select(b => new SelectListItem
            {
                Value = b.Code,
                Text = b.Name
            }).ToList();

            if (!ModelState.IsValid)
                return View(model);

            var user = await _userManager.GetUserAsync(User);
            var profile = await _context.ArtistProfiles
                .FirstOrDefaultAsync(p => p.UserId == user.Id);

            if (profile == null) return NotFound();

            var validationResult = await _paystackService.ValidateBankAccountAsync(
                model.BankCode, model.AccountNumber);

            if (!validationResult.Success)
            {
                ModelState.AddModelError("AccountNumber", validationResult.Message);
                return View(model);
            }

            model.AccountHolderName = validationResult.AccountHolderName;

            var bankName = banks.FirstOrDefault(b => b.Code == model.BankCode)?.Name ?? "";

            profile.BankName = bankName;
            profile.BankCode = model.BankCode;
            profile.AccountHolderName = validationResult.AccountHolderName;
            profile.IsBankAccountVerified = true;
            profile.BankAccountVerifiedDate = DateTime.UtcNow;

            bool isTestMode = _configuration["Paystack:Mode"]?.ToLower() != "live";

            if (isTestMode)
            {
                profile.SubaccountCode = "TEST_SUBACCOUNT_" + Guid.NewGuid().ToString().Substring(0, 8);
                await _context.SaveChangesAsync();

                TempData["Success"] = $"✅ Test mode: Bank account verified! (Subaccount not created in test mode)";
                return RedirectToAction(nameof(Banking));
            }

            var businessName = profile.FullName ?? user.Email ?? "Artist";
            var subaccountResult = await _paystackService.CreateSubaccountAsync(
                email: user.Email,
                bankCode: model.BankCode,
                accountNumber: model.AccountNumber,
                businessName: businessName,
                percentageCharge: 15m
            );

            if (!subaccountResult.Success)
            {
                ModelState.AddModelError("", subaccountResult.Message);
                model.AccountHolderName = validationResult.AccountHolderName;
                return View(model);
            }

            profile.SubaccountCode = subaccountResult.SubaccountCode;
            await _context.SaveChangesAsync();

            TempData["Success"] = $"✅ Bank account verified! Welcome aboard, {validationResult.AccountHolderName}. " +
                $"Subaccount: {subaccountResult.SubaccountCode}";

            return RedirectToAction("Profile", "Artist");
        }

        [HttpGet]
        public async Task<IActionResult> ValidateAccount(string bankCode, string accountNumber)
        {
            if (string.IsNullOrEmpty(bankCode) || string.IsNullOrEmpty(accountNumber))
                return Json(new { success = false, message = "Bank and account number are required." });

            var result = await _paystackService.ValidateBankAccountAsync(bankCode, accountNumber);

            return Json(new
            {
                success = result.Success,
                message = result.Message,
                accountHolderName = result.AccountHolderName
            });
        }

        private List<Bank> GetTestBanks()
        {
            return new List<Bank>
            {
                new Bank { Name = "ABSA (Test)", Code = "000003" },
                new Bank { Name = "Capitec (Test)", Code = "000002" },
                new Bank { Name = "FNB (Test)", Code = "000001" },
                new Bank { Name = "Standard Bank (Test)", Code = "000004" }
            };
        }

        // ═══════════════════════════════════════════════════════════
        // ARTIST UPDATE STATUS
        // ═══════════════════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ArtistUpdateStatus(int bookingId, BookingStatus newStatus, string artistNotes, decimal transportCost = 0)
        {
            try
            {
                var artistId = _userManager.GetUserId(User);

                var booking = await _context.Bookings
                    .Include(b => b.UserService)
                        .ThenInclude(us => us.Service)
                    .Include(b => b.UserService)
                        .ThenInclude(us => us.Artist)
                    .Include(b => b.Customer)
                    .Include(b => b.AvailabilitySlot)
                    .FirstOrDefaultAsync(b => b.Id == bookingId && b.UserService.ArtistId == artistId);

                if (booking == null)
                {
                    TempData["Error"] = "Booking not found or you don't have permission.";
                    return RedirectToAction(nameof(MyAppointments));
                }

                // 🔥 FIX: Check if UserService exists
                if (booking.UserService == null)
                {
                    TempData["Error"] = "This booking has missing service details. Please contact support.";
                    return RedirectToAction(nameof(MyAppointments));
                }

                booking.ArtistNotes = artistNotes;

                // ── Walk‑in bookings go to confirmation step ──
                if (newStatus == BookingStatus.Accepted && booking.SelectedLocationType == LocationType.WalkIn)
                {
                    return RedirectToAction(nameof(ConfirmAcceptWalkIn), new { bookingId = bookingId });
                }

                if (newStatus == BookingStatus.Accepted)
                {
                    if (booking.SelectedLocationType == LocationType.HouseCall && transportCost > 0)
                    {
                        booking.TransportCost = transportCost;
                        booking.TotalAmount = (booking.UserService?.Price ?? 0) + transportCost + booking.BookingFee;
                    }

                    if (booking.IsDepositPaid || booking.Status == BookingStatus.Confirmed)
                    {
                        TempData["Error"] = "This booking is already confirmed or paid.";
                        return RedirectToAction(nameof(MyAppointments));
                    }

                    booking.Status = BookingStatus.Accepted;
                    if (booking.AvailabilitySlot != null) booking.AvailabilitySlot.IsBooked = true;
                    await _context.SaveChangesAsync();

                    // ─── SEND IN-APP NOTIFICATION ───
                    try
                    {
                        if (!string.IsNullOrEmpty(booking.CustomerId))
                        {
                            await _notificationService.CreateNotificationAsync(
                                booking.CustomerId,
                                "Appointment Accepted! ✅",
                                $"Great news! {booking.UserService?.Artist?.FirstName ?? "The artist"} has ACCEPTED your appointment for {booking.UserService?.Service?.Name ?? "your service"} on {booking.AppointmentDate:MMM dd}. Pay your 50% deposit now!",
                                "booking_accepted",
                                booking.Id.ToString(),
                                Url.Action("CheckoutDeposit", "Booking", new { id = booking.Id })
                            );
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"In-app notification error: {ex.Message}"); }

                    // ─── SEND EMAIL ───
                    // ─── SEND EMAIL ───
                    if (!string.IsNullOrEmpty(booking.Customer?.Email))
                    {
                        try
                        {
                            var depositUrl = Url.Action("CheckoutDeposit", "Booking", new { id = booking.Id }, Request.Scheme);
                            string subject = "✅ Your Appointment Has Been Accepted!";
                            string emailBody = BuildAcceptanceEmail(booking, depositUrl);
                            await SendBookingStatusEmail(booking, subject, emailBody);
                        }
                        catch (Exception ex) { Console.WriteLine($"Email error: {ex.Message}"); }
                    }

                    TempData["Success"] = "Appointment accepted! Client has been notified to pay deposit.";
                }
                else if (newStatus == BookingStatus.Rejected)
                {
                    if (booking.IsDepositPaid || booking.Status == BookingStatus.Confirmed)
                    {
                        TempData["Error"] = "Cannot reject a booking that is already confirmed or paid.";
                        return RedirectToAction(nameof(MyAppointments));
                    }

                    booking.Status = BookingStatus.Rejected;
                    if (booking.AvailabilitySlot != null) booking.AvailabilitySlot.IsBooked = false;
                    await _context.SaveChangesAsync();

                    try
                    {
                        if (!string.IsNullOrEmpty(booking.CustomerId))
                        {
                            await _notificationService.CreateNotificationAsync(
                                booking.CustomerId,
                                "Appointment Declined ❌",
                                $"Unfortunately, your appointment request for {booking.UserService?.Service?.Name ?? "your service"} on {booking.AppointmentDate:MMM dd} has been declined.",
                                "booking_rejected",
                                booking.Id.ToString(),
                                Url.Action("MyBookings", "Booking")
                            );
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"In-app notification error: {ex.Message}"); }

                    TempData["Success"] = "Appointment request rejected. Client has been notified.";
                }
                else if (newStatus == BookingStatus.Completed)
                {
                    decimal totalPaid = booking.DepositPaid + booking.FinalPaymentPaid;
                    if (totalPaid < booking.TotalAmount)
                    {
                        TempData["Error"] = "Client must pay the full amount before you can mark this as completed.";
                        return RedirectToAction(nameof(MyAppointments));
                    }

                    if (booking.Status != BookingStatus.Confirmed)
                    {
                        TempData["Error"] = "Booking must be confirmed before it can be marked as completed.";
                        return RedirectToAction(nameof(MyAppointments));
                    }

                    booking.Status = BookingStatus.Completed;
                    await _context.SaveChangesAsync();

                    try
                    {
                        if (!string.IsNullOrEmpty(booking.CustomerId))
                        {
                            await _notificationService.CreateNotificationAsync(
                                booking.CustomerId,
                                "Service Completed! ⭐",
                                $"Your {booking.UserService?.Service?.Name ?? "service"} appointment has been completed. Thank you for choosing us!",
                                "booking_completed",
                                booking.Id.ToString(),
                                Url.Action("MyBookings", "Booking")
                            );
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"In-app notification error: {ex.Message}"); }

                    TempData["Success"] = "Service marked as completed! Client has been notified.";
                }

                return RedirectToAction(nameof(MyAppointments));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ArtistUpdateStatus error: {ex.Message}");
                Console.WriteLine($"❌ Stack: {ex.StackTrace}");
                TempData["Error"] = "An error occurred while updating the booking status. Please try again.";
                return RedirectToAction(nameof(MyAppointments));
            }
        }

        // ═══════════════════════════════════════════════════════════
        // CONFIRM WALK‑IN ACCEPTANCE (with location sharing)
        // ═══════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> ConfirmAcceptWalkIn(int bookingId)
        {
            var artistId = _userManager.GetUserId(User);
            var booking = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                        .ThenInclude(a => a.ArtistProfile)
                .FirstOrDefaultAsync(b => b.Id == bookingId && b.UserService.ArtistId == artistId);

            if (booking == null || booking.SelectedLocationType != LocationType.WalkIn)
                return NotFound();

            var model = new ConfirmAcceptWalkInViewModel
            {
                BookingId = booking.Id,
                ServiceName = booking.UserService?.Service?.Name ?? "Service",
                HasStudioAddress = !string.IsNullOrEmpty(booking.UserService?.Artist?.ArtistProfile?.StudioAddress)
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmAcceptWalkIn(int bookingId, bool shareLocation)
        {
            var artistId = _userManager.GetUserId(User);
            var booking = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                        .ThenInclude(a => a.ArtistProfile)
                .Include(b => b.Customer)
                .FirstOrDefaultAsync(b => b.Id == bookingId && b.UserService.ArtistId == artistId);

            if (booking == null)
                return NotFound();

            booking.IsLocationShared = shareLocation;
            booking.Status = BookingStatus.Accepted;
            booking.ArtistNotes = "Accepted with location sharing: " + (shareLocation ? "Yes" : "No");

            if (booking.AvailabilitySlot != null)
                booking.AvailabilitySlot.IsBooked = true;

            await _context.SaveChangesAsync();

            try
            {
                await _notificationService.CreateNotificationAsync(
                    booking.CustomerId,
                    "Appointment Accepted! ✅",
                    $"Great news! {booking.UserService?.Artist?.FirstName} has ACCEPTED your walk‑in appointment for {booking.UserService?.Service?.Name} on {booking.AppointmentDate:MMM dd}.",
                    "booking_accepted",
                    booking.Id.ToString(),
                    Url.Action("MyBookings", "Booking")
                );
            }
            catch (Exception ex) { Console.WriteLine($"In-app notification error: {ex.Message}"); }

            if (!string.IsNullOrEmpty(booking.Customer?.Email))
            {
                string subject = "✅ Your Walk‑in Appointment Has Been Accepted!";
                string emailBody = $@"
                <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #f0c808; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
                    <h2 style='color: #f0c808;'>✨ Appointment Accepted! ✨</h2>
                    <p>Dear {booking.Customer.FirstName},</p>
                    <p>Great news! The artist has ACCEPTED your walk‑in appointment request.</p>
                    <p><strong>Service:</strong> {booking.UserService?.Service?.Name}</p>
                    <p><strong>Date:</strong> {booking.AppointmentDate:MMMM dd, yyyy} at {booking.AppointmentDate:hh:mm tt}</p>
                    <p><strong>Total Amount:</strong> R {booking.TotalAmount:N2}</p>
                    <p><strong>Deposit Required (50%):</strong> R {(booking.TotalAmount / 2):N2}</p>
                    <div style='text-align: center; margin: 20px 0;'>
                        <a href='{Url.Action("CheckoutDeposit", "Booking", new { id = booking.Id }, Request.Scheme)}' style='background: #f0c808; color: #000; padding: 10px 20px; text-decoration: none; border-radius: 5px;'>PAY YOUR 50% DEPOSIT NOW</a>
                    </div>
                    <p>Thank you for choosing RubiOr!</p>
                </div>";

                await _commService.SendDirectMessageEmailAsync(artistId, booking.CustomerId, subject, emailBody);
            }

            TempData["Success"] = "Walk‑in appointment accepted successfully! " + (shareLocation ? "Location sharing enabled." : "Location sharing disabled.");
            return RedirectToAction(nameof(MyAppointments));
        }

        // ═══════════════════════════════════════════════════════════
        // REVIEWS
        // ═══════════════════════════════════════════════════════════
        public async Task<IActionResult> Reviews(int page = 1, int pageSize = 10)
        {
            // ─── READ FILTER DIRECTLY FROM QUERY STRING ───
            string rating = Request.Query["rating"].ToString();

            // ─── LOG FOR DEBUG ───
            Console.WriteLine($"🔍 Reviews filter - rating: '{rating}', page: {page}");

            var artistId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(artistId)) return Challenge();

            var query = _context.Reviews
                .Include(r => r.Customer)
                .Include(r => r.Booking)
                    .ThenInclude(b => b.UserService)
                        .ThenInclude(us => us.Service)
                .Where(r => r.Booking.UserService.ArtistId == artistId);

            // ─── APPLY FILTER ───
            if (!string.IsNullOrEmpty(rating) && int.TryParse(rating, out int ratingValue))
            {
                query = query.Where(r => r.Rating == ratingValue);
            }

            // ─── PAGINATION ───
            var totalCount = await query.CountAsync();

            var reviewData = await query
                .OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // ─── STATS (ALL reviews, not filtered) ───
            var allReviews = await _context.Reviews
                .Include(r => r.Booking)
                .Where(r => r.Booking.UserService.ArtistId == artistId)
                .ToListAsync();

            var stats = new
            {
                Total = allReviews.Count,
                Average = allReviews.Any() ? allReviews.Average(r => r.Rating) : 0,
                FiveStar = allReviews.Count(r => r.Rating == 5),
                FourStar = allReviews.Count(r => r.Rating == 4),
                ThreeStar = allReviews.Count(r => r.Rating == 3),
                TwoStar = allReviews.Count(r => r.Rating == 2),
                OneStar = allReviews.Count(r => r.Rating == 1)
            };

            ViewBag.Stats = stats;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            ViewBag.TotalCount = totalCount;
            ViewBag.SelectedRating = rating;

            return View(reviewData);
        }
        // ═══════════════════════════════════════════════════════════
        // SUPPORT (Artist)
        // ═══════════════════════════════════════════════════════════
        [HttpGet]
        public IActionResult Support()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitSupportReport(string category, string description, string email, List<IFormFile> attachments)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(category))
                {
                    TempData["Error"] = "Please select a category.";
                    return RedirectToAction(nameof(Support));
                }

                if (string.IsNullOrWhiteSpace(description))
                {
                    TempData["Error"] = "Please describe the issue.";
                    return RedirectToAction(nameof(Support));
                }

                if (string.IsNullOrWhiteSpace(email))
                {
                    TempData["Error"] = "Email address is required. Please enter your email so we can follow up.";
                    return RedirectToAction(nameof(Support));
                }

                if (!IsValidEmail(email))
                {
                    TempData["Error"] = "Please enter a valid email address.";
                    return RedirectToAction(nameof(Support));
                }

                var report = new SupportReport
                {
                    Category = category,
                    Description = description,
                    Email = email,
                    SubmittedAt = DateTime.UtcNow.AddHours(2)
                };
                _context.SupportReports.Add(report);
                await _context.SaveChangesAsync();

                var uploadedFilePaths = new List<string>();
                var uploadedFileUrls = new List<string>();

                if (attachments != null && attachments.Any())
                {
                    long totalSize = attachments.Sum(f => f.Length);
                    if (totalSize > 10_000_000)
                    {
                        TempData["Error"] = "Total file size exceeds 10MB. Please reduce the size and try again.";
                        return RedirectToAction(nameof(Support));
                    }

                    var uploadDir = Path.Combine(_env.WebRootPath, "uploads", "support");
                    if (!Directory.Exists(uploadDir))
                        Directory.CreateDirectory(uploadDir);

                    foreach (var file in attachments)
                    {
                        if (file.Length == 0) continue;
                        var fileName = $"{Guid.NewGuid():N}_{file.FileName}";
                        var filePath = Path.Combine(uploadDir, fileName);
                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await file.CopyToAsync(stream);
                        }
                        uploadedFilePaths.Add(filePath);
                        uploadedFileUrls.Add($"/uploads/support/{fileName}");
                    }
                }

                string subject = $"New Artist Support Report: {category}";
                string body = $@"
            <h2>New Artist Support Report</h2>
            <p><strong>Category:</strong> {category}</p>
            <p><strong>Artist Email:</strong> {email}</p>
            <p><strong>Description:</strong></p>
            <p>{description}</p>
            <p><strong>Submitted at:</strong> {DateTime.UtcNow.AddHours(2):yyyy-MM-dd HH:mm:ss}</p>
            {(uploadedFileUrls.Any() ? $"<p><strong>Attachments:</strong> {string.Join(", ", uploadedFileUrls)}</p>" : "")}
            <hr />
            <p style='color:#888;'>This report was submitted via the RubiOr artist support page.</p>
        ";

                string[] stakeholderEmails = new string[]
                {
                    "ignatiuslerefolo07101999@gmail.com",
                    "neo305mofokeng@gmail.com"
                };

                await SendEmailToMultipleAsync(stakeholderEmails, subject, body, uploadedFilePaths);

                TempData["Success"] = "Thank you! Your report has been submitted. We'll review it within 24 hours.";
                return RedirectToAction(nameof(Support));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SubmitSupportReport Error: {ex.Message}\n{ex.StackTrace}");
                TempData["Error"] = "There was an issue submitting your report. Please try again or contact us directly.";
                return RedirectToAction(nameof(Support));
            }
        }
        // ─── HELPER: Send email via communication service ───
        private async Task SendBookingStatusEmail(Booking booking, string subject, string emailBody)
        {
            if (string.IsNullOrEmpty(booking.Customer?.Email)) return;

            try
            {
                await _commService.SendDirectMessageEmailAsync(
                    booking.UserService.ArtistId,
                    booking.CustomerId,
                    subject,
                    emailBody
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Email error for booking {booking.Id}: {ex.Message}");
            }
        }

        // ─── HELPER: Build acceptance email HTML ───
        private string BuildAcceptanceEmail(Booking booking, string depositUrl)
        {
            return $@"
    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #f0c808; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
        <h2 style='color: #f0c808;'>✨ Appointment Accepted! ✨</h2>
        <p>Dear {booking.Customer?.FirstName},</p>
        <p>Great news! The artist has ACCEPTED your appointment request.</p>
        <p><strong>Service:</strong> {booking.UserService?.Service?.Name ?? "your service"}</p>
        <p><strong>Date:</strong> {booking.AppointmentDate:MMMM dd, yyyy} at {booking.AppointmentDate:hh:mm tt}</p>
        <p><strong>Service Price:</strong> R {booking.ServicePrice:N2}</p>
        <p><strong>Deposit Required (50% service + R5 fee):</strong> R {((booking.ServicePrice / 2) + booking.BookingFee):N2}</p>
        <div style='text-align: center; margin: 20px 0;'>
            <a href='{depositUrl}' style='background: #f0c808; color: #000; padding: 10px 20px; text-decoration: none; border-radius: 5px;'>PAY YOUR DEPOSIT NOW</a>
        </div>
        <p>Thank you for choosing RubiOr!</p>
    </div>";
        }

        // ─── Helpers ───
        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        private async Task SendEmailToMultipleAsync(string[] toEmails, string subject, string body, List<string> attachmentPaths = null)
        {
            var smtpClient = new SmtpClient
            {
                Host = _configuration["SmtpSettings:Host"],
                Port = int.Parse(_configuration["SmtpSettings:Port"]),
                Credentials = new NetworkCredential(
                    _configuration["SmtpSettings:Username"],
                    _configuration["SmtpSettings:Password"]
                ),
                EnableSsl = true,
                UseDefaultCredentials = false
            };

            using (var mailMessage = new MailMessage
            {
                From = new MailAddress(_configuration["SmtpSettings:FromAddress"], "RubiOr Support"),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            })
            {
                foreach (var email in toEmails)
                {
                    mailMessage.To.Add(email);
                }

                if (attachmentPaths != null && attachmentPaths.Any())
                {
                    foreach (var path in attachmentPaths)
                    {
                        if (System.IO.File.Exists(path))
                        {
                            var attachment = new Attachment(path);
                            mailMessage.Attachments.Add(attachment);
                        }
                    }
                }

                await smtpClient.SendMailAsync(mailMessage);
            }
        }
    }
}