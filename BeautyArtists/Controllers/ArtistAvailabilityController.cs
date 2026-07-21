using BeautyArtists.Data;
using BeautyArtists.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeautyArtists.Controllers
{
    [Authorize(Roles = "Artist")]
    public class ArtistAvailabilityController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public ArtistAvailabilityController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: ArtistAvailability/Index
        [HttpGet]
        public async Task<IActionResult> Index(int page = 1, int pageSize = 10)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
                return Challenge();

            var query = _context.ArtistAvailabilities
                .Where(a => a.ArtistId == userId && a.AvailableDate >= DateTime.Now.Date)
                .OrderBy(a => a.AvailableDate)
                .ThenBy(a => a.StartTime);

            var totalCount = await query.CountAsync();
            var slots = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            ViewBag.TotalCount = totalCount;

            return View(slots);
        }

        // POST: ArtistAvailability/AddSlot
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddSlot(DateTime AvailableDate, TimeSpan StartTime, TimeSpan EndTime)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
                return Challenge();

            // Validation
            if (StartTime >= EndTime)
            {
                TempData["Error"] = "Start time must be before end time.";
                return RedirectToAction(nameof(Index));
            }

            if (AvailableDate < DateTime.Now.Date)
            {
                TempData["Error"] = "Cannot add availability for past dates.";
                return RedirectToAction(nameof(Index));
            }

            // Check for overlapping slots
            var overlappingSlot = await _context.ArtistAvailabilities
                .FirstOrDefaultAsync(a => a.ArtistId == userId
                    && a.AvailableDate == AvailableDate
                    && ((StartTime >= a.StartTime && StartTime < a.EndTime)
                        || (EndTime > a.StartTime && EndTime <= a.EndTime)
                        || (StartTime <= a.StartTime && EndTime >= a.EndTime)));

            if (overlappingSlot != null)
            {
                TempData["Error"] = $"Time slot overlaps with existing slot ({overlappingSlot.StartTime:hh\\:mm} - {overlappingSlot.EndTime:hh\\:mm}).";
                return RedirectToAction(nameof(Index));
            }

            var newSlot = new ArtistAvailability
            {
                ArtistId = userId,
                AvailableDate = AvailableDate,
                StartTime = StartTime,
                EndTime = EndTime,
                IsBooked = false
            };

            try
            {
                _context.ArtistAvailabilities.Add(newSlot);
                await _context.SaveChangesAsync();
                TempData["Success"] = $"✅ Slot added for {AvailableDate:MMM dd, yyyy} from {StartTime:hh\\:mm} to {EndTime:hh\\:mm}!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Failed to add slot: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // POST: ArtistAvailability/DeleteSlot
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSlot(int id)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            var slot = await _context.ArtistAvailabilities.FindAsync(id);

            if (slot == null)
                return NotFound(new { success = false, message = "Slot not found" });

            if (slot.ArtistId != userId)
                return Unauthorized(new { success = false, message = "You don't own this slot" });

            if (slot.IsBooked)
                return BadRequest(new { success = false, message = "Cannot delete a booked slot" });

            try
            {
                _context.ArtistAvailabilities.Remove(slot);
                await _context.SaveChangesAsync();
                return Ok(new { success = true, message = "Slot deleted successfully" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }
    }
}