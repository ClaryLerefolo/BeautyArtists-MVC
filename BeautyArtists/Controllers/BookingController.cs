using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Models.ViewModels;
using BeautyArtists.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using static BeautyArtists.Models.Booking;

namespace BeautyArtists.Controllers
{
    public class BookingController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ICommunicationService _commService;

        public BookingController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, ICommunicationService commService)
        {
            _context = context;
            _userManager = userManager;
            _commService = commService;
        }

        // ══════════════════════════════════
        //  GET: Booking/Book
        // ══════════════════════════════════
        public async Task<IActionResult> Book()
        {
            var services = await _context.UserServices
                .Include(us => us.Service)
                .Include(us => us.Artist)
                    .ThenInclude(a => a.ArtistProfile)
                .AsNoTracking()
                .ToListAsync();

            return View(services);
        }

        // ═══════════════════════════════════════════════════════════
        //  GET: Booking/GetArtistAvailability - FIXED FOR CALENDAR
        // ═══════════════════════════════════════════════════════════
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetArtistAvailability(string artistId)
        {
            if (string.IsNullOrEmpty(artistId))
            {
                return Json(new List<object>());
            }

            var today = DateTime.Now.Date;

            var slots = await _context.ArtistAvailabilities
                .Where(a => a.ArtistId == artistId
                    && !a.IsBooked
                    && a.AvailableDate >= today)
                .OrderBy(a => a.AvailableDate)
                .ThenBy(a => a.StartTime)
                .Select(a => new
                {
                    id = a.Id,
                    date = a.AvailableDate.ToString("yyyy-MM-dd"),
                    timeString = $"{a.StartTime:hh\\:mm} - {a.EndTime:hh\\:mm}"
                })
                .ToListAsync();

            return Json(slots);
        }

        // ═══════════════════════════════════════════════════════════
        //  DEBUG: Check slots in database (REMOVE AFTER TESTING)
        // ═══════════════════════════════════════════════════════════
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> DebugSlots(string artistId)
        {
            if (string.IsNullOrEmpty(artistId))
                return Json(new { error = "No artistId provided" });

            var allSlots = await _context.ArtistAvailabilities
                .Where(a => a.ArtistId == artistId)
                .Select(a => new
                {
                    a.Id,
                    a.AvailableDate,
                    a.StartTime,
                    a.EndTime,
                    a.IsBooked,
                    IsFuture = a.AvailableDate >= DateTime.Now.Date,
                    CurrentDate = DateTime.Now.Date
                })
                .ToListAsync();

            var availableSlots = allSlots.Where(s => !s.IsBooked && s.AvailableDate >= DateTime.Now.Date).ToList();

            return Json(new
            {
                artistId = artistId,
                totalSlots = allSlots.Count,
                availableSlots = availableSlots.Count,
                allSlots = allSlots,
                message = availableSlots.Count == 0 ? "NO AVAILABLE SLOTS FOUND! Please add availability as an artist." : "Slots found!"
            });
        }

        // ══════════════════════════════════
        //  GET: Booking/BookService
        // ══════════════════════════════════
        [Authorize]
        public async Task<IActionResult> BookService(int userServiceId)
        {
            var userService = await _context.UserServices
                .Include(us => us.Service)
                    .ThenInclude(s => s.ServiceCategory)
                .Include(us => us.Artist)
                    .ThenInclude(a => a.ArtistProfile)
                .AsNoTracking()
                .FirstOrDefaultAsync(us => us.Id == userServiceId);

            if (userService == null) return NotFound();

            var artistName = !string.IsNullOrEmpty(userService.Artist?.FirstName)
                ? $"{userService.Artist.FirstName} {userService.Artist.LastName}".Trim()
                : userService.Artist?.UserName ?? "Pro Artist";

            var model = new BookingViewModel
            {
                UserServiceId = userServiceId,
                ServiceName = userService.Service?.Name,
                Price = userService.Price,
                ArtistName = artistName,
                ArtistId = userService.ArtistId,
                ArtistProfilePicture = userService.Artist?.ArtistProfile?.ProfilePictureUrl ?? "/images/default-profile.png",
                CategoryName = userService.Service?.ServiceCategory?.Name,
                SelectedLocationType = LocationType.WalkIn
            };

            return View("BookService", model);
        }

        // ══════════════════════════════════
        //  POST: Booking/ConfirmBooking
        // ══════════════════════════════════
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmBooking(BookingViewModel model)
        {
            if (!User.Identity.IsAuthenticated)
                return Challenge();

            var currentUser = await _userManager.GetUserAsync(User);

            // ── HOUSE CALL SPECIFIC VALIDATION ──
            if (model.SelectedLocationType == LocationType.HouseCall)
            {
                if (string.IsNullOrWhiteSpace(model.HouseCallAddress))
                {
                    ModelState.AddModelError("HouseCallAddress", "An address is required for house calls.");
                }

                if (string.IsNullOrEmpty(model.Latitude) || string.IsNullOrEmpty(model.Longitude))
                {
                    ModelState.AddModelError(string.Empty, "Please pin your exact location on the map.");
                }
            }

            // ── IF VALIDATION FAILS: REPOPULATE AND RETURN VIEW ──
            if (!ModelState.IsValid)
            {
                var userService = await _context.UserServices
                    .Include(us => us.Service)
                        .ThenInclude(s => s.ServiceCategory)
                    .Include(us => us.Artist)
                        .ThenInclude(a => a.ArtistProfile)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(us => us.Id == model.UserServiceId);

                if (userService != null)
                {
                    model.ServiceName = userService.Service?.Name;
                    model.Price = userService.Price;
                    model.ArtistId = userService.ArtistId;
                    model.ArtistName = !string.IsNullOrEmpty(userService.Artist?.FirstName)
                        ? $"{userService.Artist.FirstName} {userService.Artist.LastName}".Trim()
                        : userService.Artist?.UserName ?? "Pro Artist";
                    model.ArtistProfilePicture = userService.Artist?.ArtistProfile?.ProfilePictureUrl ?? "/images/default-profile.png";
                    model.CategoryName = userService.Service?.ServiceCategory?.Name;
                }

                return View("BookService", model);
            }

            // ── FETCH & LOCK SLOT ──
            var slot = await _context.ArtistAvailabilities
                .FirstOrDefaultAsync(a => a.Id == model.AvailabilitySlotId && !a.IsBooked);

            if (slot == null)
            {
                ModelState.AddModelError(string.Empty, "Sorry, this slot was just booked by someone else. Please select another.");

                var userService = await _context.UserServices
                    .Include(us => us.Service)
                        .ThenInclude(s => s.ServiceCategory)
                    .Include(us => us.Artist)
                        .ThenInclude(a => a.ArtistProfile)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(us => us.Id == model.UserServiceId);

                if (userService != null)
                {
                    model.ServiceName = userService.Service?.Name;
                    model.Price = userService.Price;
                    model.ArtistId = userService.ArtistId;
                    model.ArtistName = !string.IsNullOrEmpty(userService.Artist?.FirstName)
                        ? $"{userService.Artist.FirstName} {userService.Artist.LastName}".Trim()
                        : userService.Artist?.UserName ?? "Pro Artist";
                    model.ArtistProfilePicture = userService.Artist?.ArtistProfile?.ProfilePictureUrl ?? "/images/default-profile.png";
                    model.CategoryName = userService.Service?.ServiceCategory?.Name;
                }
                return View("BookService", model);
            }

            var appointmentDate = slot.AvailableDate.Add(slot.StartTime);

            // ── CREATE INITIAL BOOKING AS PENDING ──
            var booking = new Booking
            {
                CustomerId = currentUser.Id,
                UserServiceId = model.UserServiceId,
                BookingDate = DateTime.UtcNow,
                AppointmentDate = appointmentDate,
                Notes = model.Notes,
                HasRescheduled = false,
                Status = BookingStatus.Pending,
                SelectedLocationType = model.SelectedLocationType.GetValueOrDefault(LocationType.WalkIn),
                TransportCost = 0,
                TotalAmount = model.Price,
                IsDepositPaid = false,
                AvailabilitySlotId = slot.Id
            };

            if (model.SelectedLocationType == LocationType.HouseCall)
            {
                booking.HouseCallAddress = model.HouseCallAddress;
                booking.Latitude = model.Latitude;
                booking.Longitude = model.Longitude;
            }

            slot.IsBooked = true;
            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync();

            // Notify artist
            if (!string.IsNullOrEmpty(slot.ArtistId))
            {
                await _commService.SendBookingRequestToArtistAsync(slot.ArtistId, booking.Id);
            }

            if (booking.SelectedLocationType == LocationType.WalkIn)
            {
                TempData["Success"] = "Appointment requested successfully! The Artist must review and confirm your slot before deposit payment can be processed.";
            }
            else
            {
                TempData["Success"] = "House Call request sent! The Artist will review your location coordinates, apply any relevant transport costs, and confirm.";
            }

            return RedirectToAction("MyBookings");
        }

        // ══════════════════════════════════
        //  POST: Booking/ArtistUpdateStatus
        // ══════════════════════════════════
        // Update your existing ArtistUpdateStatus method in BookingController
        // ══════════════════════════════════
        //  POST: Booking/ArtistUpdateStatus - FIXED WITH EMAIL NOTIFICATIONS
        // ══════════════════════════════════
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ArtistUpdateStatus(int bookingId, Booking.BookingStatus newStatus, string artistNotes, decimal transportCost)
        {
            var booking = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.Customer)
                .FirstOrDefaultAsync(b => b.Id == bookingId);

            if (booking == null) return NotFound();

            // Store the artist's notes
            booking.ArtistNotes = artistNotes;

            // Get client email before updating
            var client = await _userManager.FindByIdAsync(booking.CustomerId);

            if (newStatus == Booking.BookingStatus.Confirmed)
            {
                // Handle transport cost for house calls
                if (booking.SelectedLocationType == LocationType.HouseCall)
                {
                    if (transportCost >= 0)
                    {
                        booking.TransportCost = transportCost;
                        booking.TotalAmount = (booking.UserService?.Price ?? 0) + transportCost;
                    }
                }

                booking.Status = BookingStatus.Confirmed;
                await _context.SaveChangesAsync();

                // ========== SEND EMAIL TO CLIENT ==========
                var depositUrl = Url.Action("CheckoutDeposit", "Booking", new { id = booking.Id }, Request.Scheme);

                string subject = "✅ Your Appointment Has Been Confirmed!";
                string emailBody = $@"
        <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #f0c808; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
            <div style='text-align: center; margin-bottom: 20px;'>
                <h1 style='color: #f0c808; margin: 0;'>✨ Appointment Confirmed! ✨</h1>
                <hr style='border-color: #f0c808;'>
            </div>
            
            <p style='font-size: 16px;'>Dear <strong>{booking.Customer?.FirstName} {booking.Customer?.LastName}</strong>,</p>
            
            <p style='font-size: 14px; color: #ddd;'>Good news! Your appointment has been <strong style='color: #28a745;'>CONFIRMED</strong> by the artist.</p>
            
            <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
                <h3 style='color: #f0c808; margin-top: 0;'>📋 Booking Details</h3>
                <p><strong>Service:</strong> {booking.UserService?.Service?.Name}</p>
                <p><strong>Artist:</strong> {booking.UserService?.Artist?.FirstName} {booking.UserService?.Artist?.LastName}</p>
                <p><strong>Date:</strong> {booking.AppointmentDate.ToString("dddd, MMMM dd, yyyy")}</p>
                <p><strong>Time:</strong> {booking.AppointmentDate.ToString("hh:mm tt")}</p>
                <p><strong>Location Type:</strong> {(booking.SelectedLocationType == LocationType.HouseCall ? "🏠 House Call" : "🏢 Walk-In")}</p>
                {(booking.SelectedLocationType == LocationType.HouseCall && !string.IsNullOrEmpty(booking.HouseCallAddress) ? $"<p><strong>📍 Address:</strong> {booking.HouseCallAddress}</p>" : "")}
            </div>
            
            <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
                <h3 style='color: #f0c808; margin-top: 0;'>💰 Payment Details</h3>
                <p><strong>Base Price:</strong> R {booking.UserService?.Price:N2}</p>
                {(booking.TransportCost > 0 ? $"<p><strong>Transport Cost:</strong> R {booking.TransportCost:N2}</p>" : "")}
                <p><strong>Total Amount:</strong> <span style='color: #f0c808; font-size: 18px;'>R {booking.TotalAmount:N2}</span></p>
                <hr style='border-color: #333;'>
                <p><strong>Deposit Required (50%):</strong> <span style='color: #ff6600;'>R {(booking.TotalAmount / 2):N2}</span></p>
            </div>
            
            {(booking.ArtistNotes != null ? $@"
            <div style='background: rgba(240, 200, 8, 0.1); padding: 15px; border-radius: 8px; margin: 15px 0; border-left: 4px solid #f0c808;'>
                <p style='margin: 0;'><strong>📝 Message from your artist:</strong></p>
                <p style='margin: 5px 0 0 0; color: #ddd; font-style: italic;'>“{booking.ArtistNotes}”</p>
            </div>" : "")}
            
            <div style='text-align: center; margin: 25px 0;'>
                <a href='{depositUrl}' style='background: linear-gradient(45deg, #f0c808, #e50914); color: #000; padding: 14px 30px; text-decoration: none; border-radius: 50px; font-weight: bold; font-size: 16px; display: inline-block;'>
                    💰 PAY YOUR 50% DEPOSIT NOW
                </a>
            </div>
            
            <div style='background: rgba(229, 9, 20, 0.1); padding: 12px; border-radius: 8px; margin: 15px 0; border-left: 4px solid #e50914;'>
                <p style='margin: 0; font-size: 12px; color: #ff8888;'>
                    <strong>⚠️ IMPORTANT:</strong> Your appointment slot is only guaranteed once the 50% deposit is paid. Please complete your payment as soon as possible.
                </p>
            </div>
            
            <hr style='border-color: #333; margin: 20px 0;'>
            <p style='font-size: 11px; color: #666; text-align: center;'>
                Need to reschedule or cancel? Please contact the artist directly through your dashboard.<br>
                &copy; {DateTime.Now.Year} Beauty Artists Hub
            </p>
        </div>";

                // Send email using your communication service
                if (client != null && !string.IsNullOrEmpty(client.Email))
                {
                    await _commService.SendDirectMessageEmailAsync(
                        booking.UserService?.ArtistId,
                        booking.CustomerId,
                        subject,
                        emailBody
                    );
                }

                TempData["Success"] = "Appointment confirmed! Client has been notified via email.";
            }
            else if (newStatus == Booking.BookingStatus.Rejected)
            {
                booking.Status = BookingStatus.Rejected;
                await _context.SaveChangesAsync();

                // Send rejection email to client
                string rejectSubject = "❌ Appointment Request Update";
                string rejectBody = $@"
        <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #e50914; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
            <h2 style='color: #e50914; text-align: center;'>Appointment Not Confirmed</h2>
            <p>Dear {booking.Customer?.FirstName},</p>
            <p>Unfortunately, your appointment request for <strong>{booking.UserService?.Service?.Name}</strong> on <strong>{booking.AppointmentDate:MMM dd, yyyy} at {booking.AppointmentDate:hh:mm tt}</strong> could not be confirmed by the artist.</p>
            {(artistNotes != null ? $"<p><strong>Reason:</strong> {artistNotes}</p>" : "")}
            <p>Please try booking a different time slot or contact the artist directly.</p>
            <hr>
            <p style='font-size: 12px; color: #666;'>Beauty Artists Hub</p>
        </div>";

                if (client != null && !string.IsNullOrEmpty(client.Email))
                {
                    await _commService.SendDirectMessageEmailAsync(
                        booking.UserService?.ArtistId,
                        booking.CustomerId,
                        rejectSubject,
                        rejectBody
                    );
                }

                // Free up the slot
                if (booking.AvailabilitySlotId.HasValue)
                {
                    var slot = await _context.ArtistAvailabilities.FirstOrDefaultAsync(a => a.Id == booking.AvailabilitySlotId.Value);
                    if (slot != null) slot.IsBooked = false;
                    await _context.SaveChangesAsync();
                }

                TempData["Success"] = "Appointment request rejected. Client has been notified.";
            }
            else if (newStatus == Booking.BookingStatus.Completed)
            {
                booking.Status = BookingStatus.Completed;
                await _context.SaveChangesAsync();

                // Send completion email to client
                string completeSubject = "🎉 Service Completed! Thank You!";
                string completeBody = $@"
        <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #28a745; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
            <h2 style='color: #28a745; text-align: center;'>Service Completed! 🎉</h2>
            <p>Dear {booking.Customer?.FirstName},</p>
            <p>Your <strong>{booking.UserService?.Service?.Name}</strong> appointment has been marked as completed.</p>
            <p>We hope you had a great experience! Please leave a review and share your feedback.</p>
            <p style='text-align: center; margin-top: 20px;'>✨ Thank you for choosing Beauty Artists Hub! ✨</p>
        </div>";

                if (client != null && !string.IsNullOrEmpty(client.Email))
                {
                    await _commService.SendDirectMessageEmailAsync(
                        booking.UserService?.ArtistId,
                        booking.CustomerId,
                        completeSubject,
                        completeBody
                    );
                }

                TempData["Success"] = "Service marked as completed! Client has been notified.";
            }
            else
            {
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("MyAppointments", "Artist");
        }

        // ══════════════════════════════════
        //  GET: Booking/CheckoutDeposit
        // ══════════════════════════════════
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> CheckoutDeposit(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);

            var booking = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .FirstOrDefaultAsync(b => b.Id == id && b.CustomerId == currentUser.Id);

            if (booking == null) return NotFound();

            if (booking.Status == BookingStatus.Pending)
            {
                TempData["Error"] = "This booking is currently pending artist approval. You can pay your 50% deposit once confirmed.";
                return RedirectToAction("MyBookings");
            }

            if (booking.IsDepositPaid || booking.Status == BookingStatus.Cancelled)
            {
                TempData["Error"] = "This booking is either already paid or has been cancelled.";
                return RedirectToAction("MyBookings");
            }

            return View(booking);
        }

        // ══════════════════════════════════
        //  POST: Booking/ProcessDepositPayment
        // ══════════════════════════════════
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessDepositPayment(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);

            var booking = await _context.Bookings
                .Include(b => b.UserService)
                .FirstOrDefaultAsync(b => b.Id == id && b.CustomerId == currentUser.Id);

            if (booking == null) return NotFound();

            if (booking.Status == BookingStatus.Pending)
            {
                TempData["Error"] = "Action Denied: You cannot process payments for an unconfirmed appointment.";
                return RedirectToAction("MyBookings");
            }

            booking.IsDepositPaid = true;
            await _context.SaveChangesAsync();

            if (booking.UserService != null && !string.IsNullOrEmpty(booking.UserService.ArtistId))
            {
                await _commService.SendDirectMessageEmailAsync(
                    currentUser.Id,
                    booking.UserService.ArtistId,
                    "Deposit Paid - Schedule Secured",
                    $"Client {currentUser.FirstName} has successfully settled the 50% deposit for Booking #{booking.Id}."
                );
            }

            TempData["Success"] = "First 50% Deposit cleared! Your appointment is locked in.";
            return RedirectToAction("MyBookings");
        }

        // ══════════════════════════════════
        //  POST: Booking/ProcessFinalPayment
        // ══════════════════════════════════
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessFinalPayment(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);

            var booking = await _context.Bookings
                .FirstOrDefaultAsync(b => b.Id == id && b.CustomerId == currentUser.Id);

            if (booking == null) return NotFound();

            if (!booking.IsDepositPaid)
            {
                TempData["Error"] = "You must pay the initial 50% deposit before fulfilling the final settlement balance.";
                return RedirectToAction("MyBookings");
            }

            if (booking.TotalAmount == 0)
            {
                TempData["Error"] = "This ledger execution balance cycle has already been fully finalized.";
                return RedirectToAction("MyBookings");
            }

            double daysUntilAppointment = (booking.AppointmentDate.Date - DateTime.Now.Date).TotalDays;
            if (daysUntilAppointment < 2)
            {
                TempData["Error"] = "Payment Processing Lockout: Final settlements must be cleared at least 2 days before the scheduled execution date.";
                return RedirectToAction("MyBookings");
            }

            booking.TotalAmount = 0;
            await _context.SaveChangesAsync();

            TempData["Success"] = "Final settlement cleared! See you at your session.";
            return RedirectToAction("MyBookings");
        }

        // ══════════════════════════════════
        //  GET: Booking/MyBookings
        // ══════════════════════════════════
        public async Task<IActionResult> MyBookings()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return Challenge();

            var bookings = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .Include(b => b.UserService.Artist)
                    .ThenInclude(a => a.ArtistProfile)
                .Where(b => b.CustomerId == currentUser.Id)
                .OrderByDescending(b => b.BookingDate)
                .AsNoTracking()
                .ToListAsync();

            return View(bookings);
        }

        // ══════════════════════════════════
        //  POST: Booking/Cancel
        // ══════════════════════════════════
        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id, string? clientNotes)
        {
            var currentUser = await _userManager.GetUserAsync(User);

            var booking = await _context.Bookings
                .Include(b => b.UserService)
                .FirstOrDefaultAsync(b => b.Id == id && b.CustomerId == currentUser.Id);

            if (booking == null ||
                booking.Status == BookingStatus.Completed ||
                booking.Status == BookingStatus.Cancelled)
                return NotFound();

            booking.Status = BookingStatus.Cancelled;
            booking.ClientNotes = clientNotes;

            if (booking.AvailabilitySlotId.HasValue)
            {
                var slot = await _context.ArtistAvailabilities
                    .FirstOrDefaultAsync(a => a.Id == booking.AvailabilitySlotId.Value);
                if (slot != null) slot.IsBooked = false;
            }

            await _context.SaveChangesAsync();

            if (booking.UserService != null && !string.IsNullOrEmpty(booking.UserService.ArtistId))
            {
                await _commService.SendDirectMessageEmailAsync(
                    currentUser.Id,
                    booking.UserService.ArtistId,
                    "Booking Cancelled By Client",
                    $"Client {currentUser.FirstName} has cancelled Booking #{booking.Id}."
                );
            }

            TempData["Success"] = "Your booking has been cancelled.";
            return RedirectToAction("MyBookings");
        }

        // ══════════════════════════════════
        //  GET: Booking/Reschedule
        // ══════════════════════════════════
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Reschedule(int id)
        {
            var currentUserId = _userManager.GetUserId(User);

            var booking = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                        .ThenInclude(s => s.ServiceCategory)
                .Include(b => b.UserService.Artist)
                    .ThenInclude(a => a.ArtistProfile)
                .FirstOrDefaultAsync(b => b.Id == id && b.CustomerId == currentUserId);

            if (booking == null || booking.HasRescheduled || booking.Status != BookingStatus.Confirmed)
                return NotFound();

            if (booking.AppointmentDate <= DateTime.Now.AddHours(24))
            {
                TempData["Error"] = "Rescheduling is only allowed at least 24 hours before your appointment.";
                return RedirectToAction("MyBookings");
            }

            string artistName = booking.UserService?.Artist?.ArtistProfile?.FullName
                ?? (!string.IsNullOrEmpty(booking.UserService?.Artist?.FirstName)
                    ? $"{booking.UserService.Artist.FirstName} {booking.UserService.Artist.LastName}".Trim()
                    : booking.UserService?.Artist?.UserName ?? "Pro Artist");

            var model = new BookingViewModel
            {
                BookingId = booking.Id,
                UserServiceId = booking.UserServiceId,
                PreferredDate = booking.AppointmentDate,
                Notes = booking.Notes,
                ServiceName = booking.UserService?.Service?.Name,
                ArtistName = artistName,
                ArtistId = booking.UserService?.ArtistId,
                Price = booking.TotalAmount
            };

            return View("Reschedule", model);
        }

        // ══════════════════════════════════
        //  POST: Booking/Reschedule
        // ══════════════════════════════════
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reschedule(BookingViewModel model)
        {
            var currentUser = await _userManager.GetUserAsync(User);

            var booking = await _context.Bookings
                .Include(b => b.UserService)
                .FirstOrDefaultAsync(b => b.Id == model.BookingId && b.CustomerId == currentUser.Id);

            if (booking == null || booking.Status != BookingStatus.Confirmed || booking.HasRescheduled)
                return NotFound();

            if (booking.AppointmentDate <= DateTime.Now.AddHours(24))
            {
                TempData["Error"] = "Rescheduling is only allowed at least 24 hours before your appointment.";
                return RedirectToAction("MyBookings");
            }

            var newSlot = await _context.ArtistAvailabilities
                .FirstOrDefaultAsync(a =>
                    a.Id == model.AvailabilitySlotId &&
                    a.ArtistId == booking.UserService.ArtistId &&
                    !a.IsBooked);

            if (newSlot == null)
            {
                TempData["Error"] = "That slot is no longer available. Please choose another.";
                return RedirectToAction("Reschedule", new { id = model.BookingId });
            }

            if (booking.AvailabilitySlotId.HasValue)
            {
                var oldSlot = await _context.ArtistAvailabilities
                    .FirstOrDefaultAsync(a => a.Id == booking.AvailabilitySlotId.Value);
                if (oldSlot != null) oldSlot.IsBooked = false;
            }

            booking.AppointmentDate = newSlot.AvailableDate.Add(newSlot.StartTime);
            booking.AvailabilitySlotId = newSlot.Id;
            booking.HasRescheduled = true;
            booking.ClientNotes = model.Notes;
            newSlot.IsBooked = true;

            await _context.SaveChangesAsync();

            if (booking.UserService != null && !string.IsNullOrEmpty(booking.UserService.ArtistId))
            {
                await _commService.SendDirectMessageEmailAsync(
                    currentUser.Id,
                    booking.UserService.ArtistId,
                    "Appointment Date Rescheduled",
                    $"Client {currentUser.FirstName} has rescheduled to {booking.AppointmentDate:MMM dd, yyyy} at {newSlot.StartTime:hh\\:mm}."
                );
            }

            TempData["Success"] = $"Rescheduled to {newSlot.AvailableDate:MMM dd} at {newSlot.StartTime:hh\\:mm}!";
            return RedirectToAction("MyBookings");
        }
    }
}