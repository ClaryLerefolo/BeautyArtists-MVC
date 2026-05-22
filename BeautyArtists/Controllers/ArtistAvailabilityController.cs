using BeautyArtists.Data;
using BeautyArtists.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeautyArtists.Controllers
{
    public class ArtistAvailabilityController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public ArtistAvailabilityController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // --- ARTIST VIEW: Manage My Schedule ---
        [Authorize(Roles = "Artist")]
        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User);
            var slots = await _context.ArtistAvailabilities
                .Where(a => a.ArtistId == userId && a.AvailableDate >= DateTime.Now.Date)
                .OrderBy(a => a.AvailableDate)
                .ThenBy(a => a.StartTime)
                .ToListAsync();

            return View(slots);
        }

        [HttpPost]
        [Authorize(Roles = "Artist")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddSlot(DateTime AvailableDate, TimeSpan StartTime, TimeSpan EndTime)
        {
            var userId = _userManager.GetUserId(User);

            if (StartTime >= EndTime)
            {
                TempData["Error"] = "Start time must be before end time.";
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

            _context.ArtistAvailabilities.Add(newSlot);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Time slot added successfully!";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [Authorize(Roles = "Artist")]
        public async Task<IActionResult> DeleteSlot(int id)
        {
            var slot = await _context.ArtistAvailabilities.FindAsync(id);
            if (slot == null || slot.ArtistId != _userManager.GetUserId(User)) return NotFound();

            _context.ArtistAvailabilities.Remove(slot);
            await _context.SaveChangesAsync();
            return Ok();
        }

        // --- CLIENT API: Fetch available slots for specific Artist ---
        [HttpGet]
        public async Task<IActionResult> GetSlots(string artistId)
        {
            var slots = await _context.ArtistAvailabilities
                .Where(a => a.ArtistId == artistId && !a.IsBooked && a.AvailableDate >= DateTime.Now.Date)
                .OrderBy(a => a.AvailableDate)
                .Select(a => new {
                    id = a.Id,
                    date = a.AvailableDate.ToString("yyyy-MM-dd"),
                    display = $"{a.AvailableDate:MMM dd}: {a.StartTime:hh\\:mm} - {a.EndTime:hh\\:mm}"
                })
                .ToListAsync();

            return Json(slots);
        }
    }
}