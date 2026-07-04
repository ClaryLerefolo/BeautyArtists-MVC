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

        public ArtistController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IWebHostEnvironment env, ICommunicationService communicationService, INotificationService notificationService, IPaystackService paystackService)
        {
            _context = context;
            _userManager = userManager;
            _env = env;
            _commService = communicationService;
            _notificationService = notificationService;
            _paystackService = paystackService;
        }

        // ═══════════════════════════════════════════════════════════
        // DASHBOARD
        // ═══════════════════════════════════════════════════════════
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

    // 🔥 Only update allowed fields – keep UserId intact
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


            // Handle image upload
            if (ProfilePicture != null && ProfilePicture.Length > 0)
    {
        // Ensure directory exists
        var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads/profile_pictures");
        if (!Directory.Exists(uploadsFolder))
        {
            Directory.CreateDirectory(uploadsFolder);
        }

        // Delete old image if exists (optional)
        if (!string.IsNullOrEmpty(profile.ProfilePictureUrl) && !profile.ProfilePictureUrl.StartsWith("/images/"))
        {
            var oldPath = Path.Combine(_env.WebRootPath, profile.ProfilePictureUrl.TrimStart('/'));
            if (System.IO.File.Exists(oldPath))
                System.IO.File.Delete(oldPath);
        }

        // Save new image
        var uniqueFileName = Guid.NewGuid().ToString() + Path.GetExtension(ProfilePicture.FileName);
        var filePath = Path.Combine(uploadsFolder, uniqueFileName);
        using (var fileStream = new FileStream(filePath, FileMode.Create))
        {
            await ProfilePicture.CopyToAsync(fileStream);
        }
        profile.ProfilePictureUrl = "/uploads/profile_pictures/" + uniqueFileName;
    }

    // 🔥 Explicitly mark only modified properties to avoid overwriting UserId
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

            // Delete old image if not default
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

            ViewBag.RawServices = available.Select(s => new {
                id = s.Id,
                name = s.Name,
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

            var baseService = await _context.Services.FindAsync(model.ServiceId);
            if (baseService == null)
            {
                TempData["Error"] = "Invalid Service Selection. Please try again.";
                return RedirectToAction(nameof(ManageServices));
            }

            model.ArtistId = userId;
            model.IsActive = true;              // ✅ FORCE ACTIVE

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

            // Remove validation for properties not in the edit form
            ModelState.Remove("ArtistId");
            ModelState.Remove("ServiceId");
            ModelState.Remove("Artist");
            ModelState.Remove("Service");
            ModelState.Remove("ImagePath");

            // Ensure IsActive is correctly bound (checkbox unchecked -> false)
            // The model binder will set IsActive to false if the checkbox is not present,
            // but we explicitly get the form value for safety.
            var isActiveForm = Request.Form["IsActive"].FirstOrDefault();
            model.IsActive = !string.IsNullOrEmpty(isActiveForm) && isActiveForm == "true";

            // Update only allowed fields
            existingService.Price = model.Price;
            existingService.Duration = model.Duration;
            existingService.CustomDescription = model.CustomDescription;
            existingService.IsActive = model.IsActive;

            if (ImageFile != null && ImageFile.Length > 0)
            {
                // Delete old image if exists
                if (!string.IsNullOrEmpty(existingService.ImagePath))
                {
                    var oldPath = Path.Combine(_env.WebRootPath, existingService.ImagePath.TrimStart('/'));
                    if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
                }

                // Save new image
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
        public async Task<IActionResult> MyAppointments()
        {
            var artistId = _userManager.GetUserId(User);

            var myBookings = await _context.Bookings
                .Include(b => b.Customer)
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService.Artist)
                .Where(b => b.UserService.ArtistId == artistId)
                .OrderByDescending(b => b.AppointmentDate)
                .ToListAsync();

            return View(myBookings);
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
        // ─── GET: Artist/Banking ───
        [HttpGet]
        public async Task<IActionResult> Banking()
        {
            var user = await _userManager.GetUserAsync(User);
            var profile = await _context.ArtistProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);

            if (profile == null)
                return NotFound();

            // ─── USE THE SAME HARDCODED BANKS ───
            var banks = new List<Bank>
    {
        new Bank { Name = "ABSA", Code = "585001" },
        new Bank { Name = "Capitec", Code = "585010" },
        new Bank { Name = "FNB", Code = "585012" },
        new Bank { Name = "Nedbank", Code = "585013" },
        new Bank { Name = "Standard Bank", Code = "585014" },
        new Bank { Name = "African Bank", Code = "585016" },
        new Bank { Name = "Bank Zero", Code = "585021" },
        new Bank { Name = "Bidvest Bank", Code = "585022" },
        new Bank { Name = "Discovery Bank", Code = "585030" },
        new Bank { Name = "TymeBank", Code = "585032" },
        new Bank { Name = "Investec", Code = "585033" },
        new Bank { Name = "Sasfin Bank", Code = "585034" },
        new Bank { Name = "Old Mutual Bank", Code = "585035" }
    };

            var model = new BankingViewModel
            {
                BankName = profile.BankName ?? "",
                BankCode = profile.SubaccountCode != null ? profile.BankCode : "", // 🔥 FIXED
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

        private List<Bank> GetHardcodedBanks()
        {
            return new List<Bank>
    {
        new Bank { Name = "ABSA", Code = "585001" },
        new Bank { Name = "Capitec", Code = "585010" },
        new Bank { Name = "FNB", Code = "585012" },
        new Bank { Name = "Nedbank", Code = "585013" },
        new Bank { Name = "Standard Bank", Code = "585014" },
        new Bank { Name = "African Bank", Code = "585016" },
        new Bank { Name = "Bank Zero", Code = "585021" },
        new Bank { Name = "Bidvest Bank", Code = "585022" },
        new Bank { Name = "Discovery Bank", Code = "585030" },
        new Bank { Name = "TymeBank", Code = "585032" },
        new Bank { Name = "Investec", Code = "585033" },
        new Bank { Name = "Sasfin Bank", Code = "585034" },
        new Bank { Name = "Old Mutual Bank", Code = "585035" }
    };
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Banking(BankingViewModel model)
        {
            // ─── ALWAYS HAVE BANKS AVAILABLE ───
            var banks = GetHardcodedBanks();
            model.Banks = banks.Select(b => new SelectListItem
            {
                Value = b.Code,
                Text = b.Name
            }).ToList();

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.GetUserAsync(User);
            var profile = await _context.ArtistProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);

            if (profile == null)
                return NotFound();

            // ─── STEP 1: VALIDATE BANK ACCOUNT (R3 FEE) ───
            var validationResult = await _paystackService.ValidateBankAccountAsync(model.BankCode, model.AccountNumber);

            if (!validationResult.Success)
            {
                ModelState.AddModelError("AccountNumber", validationResult.Message);
                return View(model);
            }

            // ─── SET THE NAME ───
            model.AccountHolderName = validationResult.AccountHolderName;

            // ─── STEP 2: CREATE SUBACCOUNT ───
            var businessName = $"{profile.FullName}";
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
                return View(model);
            }

            // ─── 🔥 FIX: GET BANK NAME FROM THE BANK CODE ───
            var bankName = banks.FirstOrDefault(b => b.Code == model.BankCode)?.Name ?? "";

            // ─── STEP 3: STORE SUBACCOUNT CODE ───
            profile.BankName = bankName; // ✅ NOW CORRECT
            profile.BankCode = model.BankCode; // ✅ SAVE THE CODE TOO
            profile.AccountHolderName = validationResult.AccountHolderName;
            profile.SubaccountCode = subaccountResult.SubaccountCode;
            profile.IsBankAccountVerified = true;
            profile.BankAccountVerifiedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            TempData["Success"] = $"✅ Bank account verified! Subaccount created: {subaccountResult.SubaccountCode}";
            return RedirectToAction(nameof(Banking));
        }
        // ═══════════════════════════════════════════════════════════
        // ARTIST UPDATE STATUS - FIXED WITH NOTIFICATIONS & CHECKS
        // ═══════════════════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ArtistUpdateStatus(int bookingId, BookingStatus newStatus, string artistNotes, decimal transportCost = 0)
        {
            var artistId = _userManager.GetUserId(User);

            var booking = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService.Artist)
                .Include(b => b.Customer)
                .Include(b => b.AvailabilitySlot)
                .FirstOrDefaultAsync(b => b.Id == bookingId && b.UserService.ArtistId == artistId);

            if (booking == null)
                return Unauthorized();

            booking.ArtistNotes = artistNotes;

            var client = booking.Customer;
            var clientEmail = client?.Email;
            var clientName = client?.FirstName ?? "Valued Client";

            if (newStatus == BookingStatus.Accepted)
            {
                // Handle transport cost for house calls
                if (booking.SelectedLocationType == LocationType.HouseCall && transportCost > 0)
                {
                    booking.TransportCost = transportCost;
                    booking.TotalAmount = (booking.UserService?.Price ?? 0) + transportCost;
                }

                // Prevent accepting if already confirmed or deposit paid
                if (booking.IsDepositPaid || booking.Status == BookingStatus.Confirmed)
                {
                    TempData["Error"] = "This booking is already confirmed or paid.";
                    return RedirectToAction(nameof(MyAppointments));
                }

                booking.Status = BookingStatus.Accepted;

                if (booking.AvailabilitySlot != null)
                {
                    booking.AvailabilitySlot.IsBooked = true;
                }

                await _context.SaveChangesAsync();

                // ========== SEND IN-APP NOTIFICATION TO CLIENT ==========
                try
                {
                    await _notificationService.CreateNotificationAsync(
                        booking.CustomerId,
                        "Appointment Accepted! ✅",
                        $"Great news! {booking.UserService?.Artist?.FirstName} has ACCEPTED your appointment for {booking.UserService?.Service?.Name} on {booking.AppointmentDate:MMM dd}. Pay your 50% deposit now!",
                        "booking_accepted",
                        booking.Id.ToString(),
                        Url.Action("CheckoutDeposit", "Booking", new { id = booking.Id })
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"In-app notification error (non-critical): {ex.Message}");
                }

                // ========== SEND EMAIL TO CLIENT ==========
                if (!string.IsNullOrEmpty(clientEmail))
                {
                    var depositUrl = Url.Action("CheckoutDeposit", "Booking", new { id = booking.Id }, Request.Scheme);
                    string subject = "✅ Your Appointment Has Been Accepted!";
                    string emailBody = $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #f0c808; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
    <h2 style='color: #f0c808;'>✨ Appointment Accepted! ✨</h2>
    <p>Dear {clientName},</p>
    <p>Great news! The artist has ACCEPTED your appointment request.</p>
    <p><strong>Service:</strong> {booking.UserService?.Service?.Name}</p>
    <p><strong>Date:</strong> {booking.AppointmentDate:MMMM dd, yyyy} at {booking.AppointmentDate:hh:mm tt}</p>
    <p><strong>Total Amount:</strong> R {booking.TotalAmount:N2}</p>
    <p><strong>Deposit Required (50%):</strong> R {(booking.TotalAmount / 2):N2}</p>
    <div style='text-align: center; margin: 20px 0;'>
        <a href='{depositUrl}' style='background: #f0c808; color: #000; padding: 10px 20px; text-decoration: none; border-radius: 5px;'>PAY YOUR 50% DEPOSIT NOW</a>
    </div>
    <p>Thank you for choosing Beauty Artists Hub!</p>
</div>";

                    await _commService.SendDirectMessageEmailAsync(artistId, booking.CustomerId, subject, emailBody);
                }

                TempData["Success"] = "Appointment accepted! Client has been notified to pay deposit.";
            }
            else if (newStatus == BookingStatus.Rejected)
            {
                // Prevent rejecting if already confirmed or deposit paid
                if (booking.IsDepositPaid || booking.Status == BookingStatus.Confirmed)
                {
                    TempData["Error"] = "Cannot reject a booking that is already confirmed or paid.";
                    return RedirectToAction(nameof(MyAppointments));
                }

                booking.Status = BookingStatus.Rejected;

                if (booking.AvailabilitySlot != null)
                {
                    booking.AvailabilitySlot.IsBooked = false;
                }

                await _context.SaveChangesAsync();

                try
                {
                    await _notificationService.CreateNotificationAsync(
                        booking.CustomerId,
                        "Appointment Declined ❌",
                        $"Unfortunately, your appointment request for {booking.UserService?.Service?.Name} on {booking.AppointmentDate:MMM dd} has been declined.",
                        "booking_rejected",
                        booking.Id.ToString(),
                        Url.Action("MyBookings", "Booking")
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"In-app notification error (non-critical): {ex.Message}");
                }

                TempData["Success"] = "Appointment request rejected. Client has been notified.";
            }
            else if (newStatus == BookingStatus.Completed)
            {
                // 🔥 FIXED: Check if fully paid using DepositPaid + FinalPaymentPaid
                decimal totalPaid = booking.DepositPaid + booking.FinalPaymentPaid;
                if (totalPaid < booking.TotalAmount)
                {
                    TempData["Error"] = "Client must pay the full amount before you can mark this as completed.";
                    return RedirectToAction(nameof(MyAppointments));
                }

                // Prevent completing if not confirmed
                if (booking.Status != BookingStatus.Confirmed)
                {
                    TempData["Error"] = "Booking must be confirmed before it can be marked as completed.";
                    return RedirectToAction(nameof(MyAppointments));
                }

                booking.Status = BookingStatus.Completed;
                await _context.SaveChangesAsync();

                try
                {
                    await _notificationService.CreateNotificationAsync(
                        booking.CustomerId,
                        "Service Completed! ⭐",
                        $"Your {booking.UserService?.Service?.Name} appointment has been completed. Thank you for choosing us!",
                        "booking_completed",
                        booking.Id.ToString(),
                        Url.Action("MyBookings", "Booking")
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"In-app notification error (non-critical): {ex.Message}");
                }

                TempData["Success"] = "Service marked as completed! Client has been notified.";
            }

            return RedirectToAction(nameof(MyAppointments));
        }
        // ═══════════════════════════════════════════════════════════
        // REVIEWS
        // ═══════════════════════════════════════════════════════════
        public async Task<IActionResult> Reviews()
        {
            var artistId = _userManager.GetUserId(User);

            var reviews = await _context.Reviews
                .Include(r => r.Customer)
                .Include(r => r.Booking)
                    .ThenInclude(b => b.UserService)
                        .ThenInclude(us => us.Service)
                .Where(r => r.Booking.UserService.ArtistId == artistId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            var stats = new
            {
                Total = reviews.Count,
                Average = reviews.Any() ? reviews.Average(r => r.Rating) : 0,
                FiveStar = reviews.Count(r => r.Rating == 5),
                FourStar = reviews.Count(r => r.Rating == 4),
                ThreeStar = reviews.Count(r => r.Rating == 3),
                TwoStar = reviews.Count(r => r.Rating == 2),
                OneStar = reviews.Count(r => r.Rating == 1)
            };

            ViewBag.Stats = stats;
            return View(reviews);
        }
    }
}
