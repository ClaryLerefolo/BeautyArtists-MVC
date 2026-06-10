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


        public ArtistController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IWebHostEnvironment env, ICommunicationService communicationService)
        {
            _context = context;
            _userManager = userManager;
            _env = env;
            _commService = communicationService;
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
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService.Artist)
                .Where(b => b.UserService.ArtistId == artistId)
                .OrderByDescending(b => b.AppointmentDate)
                .ToListAsync();

            return View(myBookings);
        }

        // UPDATE TRANSPORT COST ONLY
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

            // Update transport cost
            booking.TransportCost = transportCost;
            booking.TotalAmount = (booking.UserService?.Price ?? 0) + transportCost;

            await _context.SaveChangesAsync();

            // Notify client about transport cost addition
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

        // MAIN ARTIST UPDATE STATUS WITH EMAILS - THIS IS THE ONE YOUR VIEW CALLS
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

            // Save the artist's notes
            booking.ArtistNotes = artistNotes;

            // Get client info for email
            var client = booking.Customer;
            var clientEmail = client?.Email;
            var clientName = client?.FirstName ?? "Valued Client";

            if (newStatus == BookingStatus.Confirmed)
            {
                // Handle transport cost for house calls
                if (booking.SelectedLocationType == LocationType.HouseCall && transportCost > 0)
                {
                    booking.TransportCost = transportCost;
                    booking.TotalAmount = (booking.UserService?.Price ?? 0) + transportCost;
                }

                booking.Status = BookingStatus.Confirmed;

                if (booking.AvailabilitySlot != null)
                {
                    booking.AvailabilitySlot.IsBooked = true;
                }

                await _context.SaveChangesAsync();

                // ========== SEND CONFIRMATION EMAIL TO CLIENT ==========
                if (!string.IsNullOrEmpty(clientEmail))
                {
                    var depositUrl = Url.Action("CheckoutDeposit", "Booking", new { id = booking.Id }, Request.Scheme);

                    string subject = "✅ Your Appointment Has Been Confirmed!";
                    string emailBody = $@"
                    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #f0c808; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
                        <div style='text-align: center; margin-bottom: 20px;'>
                            <h1 style='color: #f0c808; margin: 0;'>✨ Appointment Confirmed! ✨</h1>
                            <hr style='border-color: #f0c808;'>
                        </div>
                        
                        <p>Dear <strong>{clientName}</strong>,</p>
                        
                        <p>Good news! Your appointment has been <strong style='color: #28a745;'>CONFIRMED</strong> by the artist.</p>
                        
                        <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
                            <h3 style='color: #f0c808; margin-top: 0;'>📋 Booking Details</h3>
                            <p><strong>Service:</strong> {booking.UserService?.Service?.Name}</p>
                            <p><strong>Artist:</strong> {booking.UserService?.Artist?.FirstName} {booking.UserService?.Artist?.LastName}</p>
                            <p><strong>Date:</strong> {booking.AppointmentDate:dddd, MMMM dd, yyyy}</p>
                            <p><strong>Time:</strong> {booking.AppointmentDate:hh:mm tt}</p>
                            <p><strong>Location Type:</strong> {(booking.SelectedLocationType == LocationType.HouseCall ? "🏠 House Call" : "🏢 Walk-In")}</p>
                            {(booking.SelectedLocationType == LocationType.HouseCall && !string.IsNullOrEmpty(booking.HouseCallAddress) ? $"<p><strong>📍 Address:</strong> {booking.HouseCallAddress}</p>" : "")}
                        </div>
                        
                        <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
                            <h3 style='color: #f0c808; margin-top: 0;'>💰 Payment Details</h3>
                            <p><strong>Base Price:</strong> R {(booking.UserService?.Price ?? 0):N2}</p>
                            {(booking.TransportCost > 0 ? $"<p><strong>Transport Cost:</strong> R {booking.TransportCost:N2}</p>" : "")}
                            <p><strong>Total Amount:</strong> <span style='color: #f0c808; font-size: 18px;'>R {booking.TotalAmount:N2}</span></p>
                            <hr style='border-color: #333;'>
                            <p><strong>Deposit Required (50%):</strong> <span style='color: #ff6600;'>R {(booking.TotalAmount / 2):N2}</span></p>
                        </div>
                        
                        {(booking.ArtistNotes != null ? $@"
                        <div style='background: rgba(240, 200, 8, 0.1); padding: 15px; border-radius: 8px; margin: 15px 0; border-left: 4px solid #f0c808;'>
                            <p><strong>📝 Message from your artist:</strong></p>
                            <p style='color: #ddd; font-style: italic;'>“{booking.ArtistNotes}”</p>
                        </div>" : "")}
                        
                        <div style='text-align: center; margin: 25px 0;'>
                            <a href='{depositUrl}' style='background: linear-gradient(45deg, #f0c808, #e50914); color: #000; padding: 14px 30px; text-decoration: none; border-radius: 50px; font-weight: bold; display: inline-block;'>
                                💰 PAY YOUR 50% DEPOSIT NOW
                            </a>
                        </div>
                        
                        <div style='background: rgba(229, 9, 20, 0.1); padding: 12px; border-radius: 8px; margin: 15px 0; border-left: 4px solid #e50914;'>
                            <p style='margin: 0; font-size: 12px; color: #ff8888;'>
                                <strong>⚠️ IMPORTANT:</strong> Your appointment slot is only guaranteed once the 50% deposit is paid.
                            </p>
                        </div>
                        
                        <hr>
                        <p style='font-size: 11px; color: #666; text-align: center;'>
                            &copy; {DateTime.Now.Year} Beauty Artists Hub
                        </p>
                    </div>";

                    await _commService.SendDirectMessageEmailAsync(artistId, booking.CustomerId, subject, emailBody);
                }

                TempData["Success"] = "Appointment confirmed! Client has been notified via email.";
            }
            else if (newStatus == BookingStatus.Rejected)
            {
                booking.Status = BookingStatus.Rejected;

                // Free up the slot
                if (booking.AvailabilitySlot != null)
                {
                    booking.AvailabilitySlot.IsBooked = false;
                }

                await _context.SaveChangesAsync();

                // ========== SEND REJECTION EMAIL TO CLIENT ==========
                if (!string.IsNullOrEmpty(clientEmail))
                {
                    string rejectSubject = "❌ Appointment Request Update";
                    string rejectBody = $@"
                    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #e50914; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
                        <h2 style='color: #e50914; text-align: center;'>Appointment Not Confirmed</h2>
                        <p>Dear {clientName},</p>
                        <p>Unfortunately, your appointment request for <strong>{booking.UserService?.Service?.Name}</strong> on <strong>{booking.AppointmentDate:MMM dd, yyyy} at {booking.AppointmentDate:hh:mm tt}</strong> could not be confirmed by the artist.</p>
                        {(artistNotes != null ? $"<p><strong>Reason:</strong> {artistNotes}</p>" : "<p>No specific reason was provided.</p>")}
                        <p>Please try booking a different time slot or contact the artist directly.</p>
                        <hr>
                        <p style='font-size: 12px; color: #666;'>Beauty Artists Hub</p>
                    </div>";

                    await _commService.SendDirectMessageEmailAsync(artistId, booking.CustomerId, rejectSubject, rejectBody);
                }

                TempData["Success"] = "Appointment request rejected. Client has been notified.";
            }
            else if (newStatus == BookingStatus.Completed)
            {
                booking.Status = BookingStatus.Completed;
                await _context.SaveChangesAsync();

                // ========== SEND COMPLETION EMAIL TO CLIENT ==========
                if (!string.IsNullOrEmpty(clientEmail))
                {
                    string completeSubject = "🎉 Service Completed! Thank You!";
                    string completeBody = $@"
                    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #28a745; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
                        <h2 style='color: #28a745; text-align: center;'>Service Completed! 🎉</h2>
                        <p>Dear {clientName},</p>
                        <p>Your <strong>{booking.UserService?.Service?.Name}</strong> appointment has been marked as completed.</p>
                        <p>We hope you had a great experience! Thank you for choosing Beauty Artists Hub!</p>
                        <p style='text-align: center; margin-top: 20px;'>✨ We hope to see you again soon! ✨</p>
                    </div>";

                    await _commService.SendDirectMessageEmailAsync(artistId, booking.CustomerId, completeSubject, completeBody);
                }

                TempData["Success"] = "Service marked as completed! Client has been notified.";
            }

            return RedirectToAction(nameof(MyAppointments));
        }

        
    }
}


