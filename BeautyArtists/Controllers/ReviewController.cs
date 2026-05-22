using BeautyArtists.Models;
using BeautyArtists.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeautyArtists.Controllers
{
    [Authorize(Roles = "Customer")]
    public class ReviewController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public ReviewController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        [HttpPost]
        public async Task<IActionResult> Submit(Review review)
        {
            if (!ModelState.IsValid) return BadRequest();
            var service = await _context.UserServices
    .Include(us => us.Service)
        .ThenInclude(s => s.Reviews)
            .ThenInclude(r => r.Customer)
    .Include(us => us.Artist)
        .ThenInclude(a => a.ArtistProfile)
    .FirstOrDefaultAsync(us => us.Id == review.ServiceId);

            if (service == null)
            {
                return NotFound();
            }


            var user = await _userManager.GetUserAsync(User);
            review.CustomerId = user.Id;
            review.CreatedAt = DateTime.Now;

            _context.Reviews.Add(review);
            await _context.SaveChangesAsync();

            return RedirectToAction("Details", "Services", new { id = review.ServiceId });
        }
    }
}
