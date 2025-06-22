using BeautyArtists.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace BeautyArtists.Controllers
{
    public class CustomerPortfolioController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CustomerPortfolioController(ApplicationDbContext context)
        {
            _context = context;
        }

        // INDEX - List portfolios for customers with optional filters
        public async Task<IActionResult> Index(string artistId, string category, bool featured = false)
        {
            var portfolios = _context.Portfolios
                .Include(p => p.Artist)
                    .ThenInclude(a => a.ArtistProfile)
                .Include(p => p.Items)
                    .ThenInclude(i => i.Category)
                .AsQueryable();

            if (!string.IsNullOrEmpty(artistId))
                portfolios = portfolios.Where(p => p.ArtistId == artistId);

            if (!string.IsNullOrEmpty(category))
                portfolios = portfolios.Where(p => p.Items.Any(i => i.Category != null && i.Category.Name == category));

            if (featured)
                portfolios = portfolios.Where(p => p.Items.Any(i => i.IsFeatured));

            // ✅ Load distinct category names
            var categoryList = await _context.PortfolioItems
                .Include(i => i.Category)
                .Where(i => i.Category != null)
                .Select(i => i.Category!.Name)
                .Distinct()
                .ToListAsync();

            // ✅ Load artists
            var artistList = await _context.Users
                .Where(u => u.ArtistProfile != null)
                .Select(u => new {
                    u.Id,
                    Name = u.ArtistProfile.FullName ?? (u.FirstName + " " + u.LastName) ?? u.UserName ?? u.Email
                })
                .ToListAsync();

            ViewBag.Categories = new SelectList(categoryList, category, category);
            ViewBag.Artists = new SelectList(artistList, "Id", "Name", artistId);

            return View(await portfolios.ToListAsync());
        }


        // DETAILS - View a single portfolio and its items
        public async Task<IActionResult> Details(int id)
        {
            var portfolio = await _context.Portfolios
                .Include(p => p.Artist)
                    .ThenInclude(a => a.ArtistProfile)
                .Include(p => p.Items)
                    .ThenInclude(i => i.Category)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (portfolio == null)
                return NotFound();

            return View(portfolio);
        }
    }
}
