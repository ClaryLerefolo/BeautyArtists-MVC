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
        private readonly INotificationService _notificationService;

        public BookingController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, ICommunicationService commService, INotificationService notificationService)
        {
            _context = context;
            _userManager = userManager;
            _commService = commService;
            _notificationService = notificationService;
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
        //  POST: Booking/ConfirmBooking - SIMPLE FIXED VERSION
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

            // ── FETCH SLOT ──
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

            // 🔥 SIMPLE FIX: Save booking FIRST, then mark slot as booked
            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync();

            // Only mark slot as booked AFTER booking is successfully saved
            slot.IsBooked = true;
            await _context.SaveChangesAsync();

            // 🔔 IN-APP NOTIFICATION: New booking request to artist
            await _notificationService.CreateNotificationAsync(
                slot.ArtistId,
                "New Booking Request! 📅",
                $"{currentUser.FirstName} has requested {booking.UserService?.Service?.Name} on {appointmentDate:MMM dd} at {appointmentDate:hh:mm tt}",
                "booking_pending",
                booking.Id.ToString(),
                Url.Action("MyAppointments", "Artist")
            );

            // 🔔 IN-APP NOTIFICATION: Booking confirmation to client
            await _notificationService.CreateNotificationAsync(
                currentUser.Id,
                "Booking Request Sent! 📤",
                $"Your request for {booking.UserService?.Service?.Name} has been sent. You'll be notified when the artist responds.",
                "booking_pending",
                booking.Id.ToString(),
                Url.Action("MyBookings", "Booking")
            );

            // Email notification to artist
            if (!string.IsNullOrEmpty(slot.ArtistId))
            {
                await _commService.SendBookingRequestToArtistAsync(slot.ArtistId, booking.Id);
            }

            if (booking.SelectedLocationType == LocationType.WalkIn)
            {
                TempData["Success"] = "Appointment requested successfully! The Artist must review and accept your slot before deposit payment can be processed.";
            }
            else
            {
                TempData["Success"] = "House Call request sent! The Artist will review your location coordinates, apply any relevant transport costs, and accept.";
            }

            return RedirectToAction("MyBookings");
        }

        // ══════════════════════════════════
        //  POST: Booking/ArtistUpdateStatus - FIXED WITH ACCEPTED STATUS
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

            // Get client for email
            var client = await _userManager.FindByIdAsync(booking.CustomerId);

            // ========== ARTIST ACCEPTS THE BOOKING ==========
            if (newStatus == Booking.BookingStatus.Accepted)
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

                booking.Status = BookingStatus.Accepted;
                await _context.SaveChangesAsync();

                // ========== SEND EMAIL TO CLIENT ==========
                var depositUrl = Url.Action("CheckoutDeposit", "Booking", new { id = booking.Id }, Request.Scheme);

                string subject = "✅ Your Appointment Has Been Accepted!";
                string emailBody = $@"
        <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #f0c808; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
            <div style='text-align: center; margin-bottom: 20px;'>
                <h1 style='color: #f0c808; margin: 0;'>✨ Appointment Accepted! ✨</h1>
                <hr style='border-color: #f0c808;'>
            </div>
            
            <p style='font-size: 16px;'>Dear <strong>{booking.Customer?.FirstName} {booking.Customer?.LastName}</strong>,</p>
            
            <p style='font-size: 14px; color: #ddd;'>Great news! The artist has <strong style='color: #28a745;'>ACCEPTED</strong> your appointment request.</p>
            
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
                    <strong>⚠️ IMPORTANT:</strong> Your appointment is not confirmed until the 50% deposit is paid. Please complete your payment as soon as possible.
                </p>
            </div>
            
            <hr style='border-color: #333; margin: 20px 0;'>
            <p style='font-size: 11px; color: #666; text-align: center;'>
                Need to reschedule or cancel? Please contact the artist directly through your dashboard.<br>
                &copy; {DateTime.Now.Year} Beauty Artists Hub
            </p>
        </div>";

                // Send email
                if (client != null && !string.IsNullOrEmpty(client.Email))
                {
                    await _commService.SendDirectMessageEmailAsync(
                        booking.UserService?.ArtistId,
                        booking.CustomerId,
                        subject,
                        emailBody
                    );
                }

                // 🔔 IN-APP NOTIFICATION: To client - Appointment Accepted
                await _notificationService.CreateNotificationAsync(
                    booking.CustomerId,
                    "Appointment Accepted! ✅",
                    $"Great news! Your appointment for {booking.UserService?.Service?.Name} on {booking.AppointmentDate:MMM dd} has been ACCEPTED. Pay your 50% deposit now!",
                    "booking_accepted",
                    booking.Id.ToString(),
                    Url.Action("CheckoutDeposit", "Booking", new { id = booking.Id })
                );

                // 🔔 IN-APP NOTIFICATION: To artist - Confirmation
                await _notificationService.CreateNotificationAsync(
                    booking.UserService.ArtistId,
                    "You Accepted an Appointment 🎉",
                    $"You accepted {booking.Customer?.FirstName}'s appointment for {booking.UserService?.Service?.Name} on {booking.AppointmentDate:MMM dd}.",
                    "booking_accepted",
                    booking.Id.ToString(),
                    Url.Action("MyAppointments", "Artist")
                );

                TempData["Success"] = "Appointment accepted! Client has been notified to pay deposit.";
            }
            else if (newStatus == Booking.BookingStatus.Rejected)
            {
                booking.Status = BookingStatus.Rejected;
                await _context.SaveChangesAsync();

                // Send rejection email to client
                string rejectSubject = "❌ Appointment Request Update";
                string rejectBody = $@"
        <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #e50914; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
            <h2 style='color: #e50914; text-align: center;'>Appointment Not Accepted</h2>
            <p>Dear {booking.Customer?.FirstName},</p>
            <p>Unfortunately, your appointment request for <strong>{booking.UserService?.Service?.Name}</strong> on <strong>{booking.AppointmentDate:MMM dd, yyyy} at {booking.AppointmentDate:hh:mm tt}</strong> has been declined.</p>
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

                // 🔔 IN-APP NOTIFICATION: To client - Appointment Rejected
                await _notificationService.CreateNotificationAsync(
                    booking.CustomerId,
                    "Appointment Declined ❌",
                    $"Unfortunately, your appointment request for {booking.UserService?.Service?.Name} on {booking.AppointmentDate:MMM dd} has been declined.",
                    "booking_rejected",
                    booking.Id.ToString(),
                    Url.Action("MyBookings", "Booking")
                );

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
            <p>We hope you had a great experience! Thank you for choosing Beauty Artists Hub!</p>
            <p style='text-align: center; margin-top: 20px;'>✨ We hope to see you again soon! ✨</p>
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

                // 🔔 IN-APP NOTIFICATION: To client - Service Completed
                await _notificationService.CreateNotificationAsync(
                    booking.CustomerId,
                    "Service Completed! ⭐",
                    $"Your {booking.UserService?.Service?.Name} appointment has been completed. Thank you for choosing us!",
                    "booking_completed",
                    booking.Id.ToString(),
                    Url.Action("MyBookings", "Booking")
                );

                TempData["Success"] = "Service marked as completed! Client has been notified.";
            }

            return RedirectToAction("MyAppointments", "Artist");
        }

        // ══════════════════════════════════
        //  GET: Booking/CheckoutDeposit – NEW (Paystack ready)
        // ══════════════════════════════════
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> CheckoutDeposit(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var booking = await _context.Bookings
         .Include(b => b.UserService)
             .ThenInclude(us => us.Service)
         .Include(b => b.UserService.Artist)          // ← ADD THIS LINE
             .ThenInclude(a => a.ArtistProfile)      // Optional
         .FirstOrDefaultAsync(b => b.Id == id && b.CustomerId == currentUser.Id);

            if (booking == null) return NotFound();


            if (booking.Status != BookingStatus.Accepted)
            {
                TempData["Error"] = "This booking must be accepted by the artist before payment.";
                return RedirectToAction("MyBookings");
            }

            if (booking.IsDepositPaid)
            {
                TempData["Error"] = "Deposit already paid for this booking.";
                return RedirectToAction("MyBookings");
            }

            var model = new CheckoutViewModel
            {
                Booking = booking,
                DepositAmount = booking.TotalAmount / 2,
                UserEmail = currentUser.Email,
                UserName = $"{currentUser.FirstName} {currentUser.LastName}"
            };

            return View(model);
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
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Artist)
                .Include(b => b.UserService.Service)
                .FirstOrDefaultAsync(b => b.Id == id && b.CustomerId == currentUser.Id);

            if (booking == null) return NotFound();

            if (!booking.IsDepositPaid)
            {
                TempData["Error"] = "You must pay the initial 50% deposit before fulfilling the final settlement balance.";
                return RedirectToAction("MyBookings");
            }

            if (booking.TotalAmount == 0)
            {
                TempData["Error"] = "This booking has already been fully paid.";
                return RedirectToAction("MyBookings");
            }

            double daysUntilAppointment = (booking.AppointmentDate.Date - DateTime.Now.Date).TotalDays;
            if (daysUntilAppointment < 2)
            {
                TempData["Error"] = "Final payment must be cleared at least 2 days before the scheduled execution date.";
                return RedirectToAction("MyBookings");
            }

            // Calculate remaining balance
            decimal remainingBalance = booking.TotalAmount / 2;

            booking.TotalAmount = 0;
            await _context.SaveChangesAsync();

            // 🔔 IN-APP NOTIFICATION: To artist - Final Payment Received
            await _notificationService.CreateNotificationAsync(
                booking.UserService.ArtistId,
                "Final Payment Received! 💵",
                $"{currentUser.FirstName} has paid the remaining balance of R{remainingBalance:N2} for {booking.UserService?.Service?.Name}.",
                "payment_received",
                booking.Id.ToString(),
                Url.Action("MyAppointments", "Artist")
            );

            // 📧 SEND EMAIL TO ARTIST
            var artist = booking.UserService?.Artist;
            if (artist != null && !string.IsNullOrEmpty(artist.Email))
            {
                string artistSubject = "💰 Final Payment Received – Appointment Fully Paid!";
                string artistBody = $@"
        <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #28a745; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
            <h2 style='color: #28a745; text-align: center;'>Final Payment Received! ✅</h2>
            <p>Dear {artist.FirstName},</p>
            <p>The client <strong>{currentUser.FirstName} {currentUser.LastName}</strong> has paid the remaining balance of <strong>R{remainingBalance:N2}</strong> for:</p>
            <div style='background: #1a1a1a; padding: 15px; border-radius: 8px; margin: 15px 0;'>
                <p><strong>Service:</strong> {booking.UserService?.Service?.Name}</p>
                <p><strong>Date:</strong> {booking.AppointmentDate:dddd, MMMM dd, yyyy}</p>
                <p><strong>Time:</strong> {booking.AppointmentDate:hh:mm tt}</p>
                <p><strong>Total Paid:</strong> <span style='color: #28a745;'>R {(booking.UserService?.Price ?? 0):N2}</span></p>
            </div>
            <p>This appointment is now <strong>FULLY PAID</strong>. You can now mark it as completed after the service is done.</p>
            <hr>
            <p style='font-size: 12px; color: #666;'>Beauty Artists Hub</p>
        </div>";

                await _commService.SendDirectMessageEmailAsync(currentUser.Id, artist.Id, artistSubject, artistBody);
            }

            // 📧 SEND EMAIL TO CLIENT (Confirmation)
            string clientSubject = "✅ Final Payment Confirmed!";
            string clientBody = $@"
    <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; border: 2px solid #28a745; border-radius: 12px; padding: 20px; background: #0a0a0a; color: #fff;'>
        <h2 style='color: #28a745; text-align: center;'>Final Payment Confirmed! 🎉</h2>
        <p>Dear {currentUser.FirstName},</p>
        <p>Your final payment of <strong>R{remainingBalance:N2}</strong> has been received.</p>
        <p>Your appointment for <strong>{booking.UserService?.Service?.Name}</strong> on <strong>{booking.AppointmentDate:dddd, MMMM dd, yyyy} at {booking.AppointmentDate:hh:mm tt}</strong> is now <strong>FULLY PAID</strong>.</p>
        <p>Thank you for choosing Beauty Artists Hub!</p>
        <hr>
        <p style='font-size: 12px; color: #666;'>Beauty Artists Hub</p>
    </div>";

            await _commService.SendDirectMessageEmailAsync(artist?.Id, currentUser.Id, clientSubject, clientBody);

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

            // Email notification to artist
            if (booking.UserService != null && !string.IsNullOrEmpty(booking.UserService.ArtistId))
            {
                await _commService.SendDirectMessageEmailAsync(
                    currentUser.Id,
                    booking.UserService.ArtistId,
                    "Booking Cancelled By Client",
                    $"Client {currentUser.FirstName} has cancelled Booking #{booking.Id}."
                );
            }

            // 🔔 IN-APP NOTIFICATION: To artist
            await _notificationService.CreateNotificationAsync(
                booking.UserService.ArtistId,
                "Booking Cancelled ❌",
                $"{currentUser.FirstName} has cancelled their booking for {booking.UserService?.Service?.Name} on {booking.AppointmentDate:MMM dd}.",
                "booking_cancelled",
                booking.Id.ToString(),
                Url.Action("MyAppointments", "Artist")
            );

            // 🔔 IN-APP NOTIFICATION: To client
            await _notificationService.CreateNotificationAsync(
                currentUser.Id,
                "Booking Cancelled ❌",
                $"You have cancelled your appointment for {booking.UserService?.Service?.Name}.",
                "booking_cancelled",
                booking.Id.ToString(),
                Url.Action("MyBookings", "Booking")
            );

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

            // 🔔 IN-APP NOTIFICATION: To client - Appointment Rescheduled
            await _notificationService.CreateNotificationAsync(
                booking.CustomerId,
                "Appointment Rescheduled 🔄",
                $"Your appointment for {booking.UserService?.Service?.Name} has been rescheduled to {newSlot.AvailableDate:MMM dd} at {newSlot.StartTime:hh\\:mm}.",
                "booking_rescheduled",
                booking.Id.ToString(),
                Url.Action("MyBookings", "Booking")
            );

            // 🔔 IN-APP NOTIFICATION: To artist - Appointment Rescheduled
            await _notificationService.CreateNotificationAsync(
                booking.UserService.ArtistId,
                "Appointment Rescheduled 🔄",
                $"{currentUser.FirstName} has rescheduled their appointment to {newSlot.AvailableDate:MMM dd} at {newSlot.StartTime:hh\\:mm}.",
                "booking_rescheduled",
                booking.Id.ToString(),
                Url.Action("MyAppointments", "Artist")
            );

            // Email notification to artist
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

        // ══════════════════════════════════
        //  GET: Booking/CheckoutFinalPayment
        // ══════════════════════════════════
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> CheckoutFinalPayment(int id)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var booking = await _context.Bookings
                .Include(b => b.UserService)
                    .ThenInclude(us => us.Service)
                .FirstOrDefaultAsync(b => b.Id == id && b.CustomerId == currentUser.Id);

            if (booking == null) return NotFound();

            if (booking.Status != BookingStatus.Confirmed)
            {
                TempData["Error"] = "This booking must be confirmed before final payment.";
                return RedirectToAction("MyBookings");
            }
            decimal remainingBalance = booking.TotalAmount / 2;

            if (booking.TotalAmount == 0)
            {
                TempData["Error"] = "No remaining balance to pay.";
                return RedirectToAction("MyBookings");
            }

            double daysUntilAppointment = (booking.AppointmentDate.Date - DateTime.Now.Date).TotalDays;
            if (daysUntilAppointment < 2)
            {
                TempData["Error"] = "Final payment must be cleared at least 2 days before the appointment.";
                return RedirectToAction("MyBookings");
            }

            var model = new CheckoutViewModel
            {
                Booking = booking,
                DepositAmount = remainingBalance, // 🔥 Fixed: remaining 50%
                UserEmail = currentUser.Email,
                UserName = $"{currentUser.FirstName} {currentUser.LastName}"
            };

            return View("CheckoutFinalPayment", model);
        }
    }
}