using BeautyArtists.Data;
using BeautyArtists.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeautyArtists.Controllers
{
    public class CustomerPortfolioItemController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CustomerPortfolioItemController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: CustomerPortfolioItem/Index
        public async Task<IActionResult> Index(int? artistId, int? categoryId, bool? isFeatured)
        {
            var query = _context.PortfolioItems
                .Include(p => p.Artist)
                .ThenInclude(a => a.ArtistProfile)
                .Include(p => p.CategoryId)
                .Include(p => p.Portfolio)
                .AsQueryable();

            if (artistId.HasValue)
            {
                query = query.Where(p => p.Artist.Id == artistId.ToString());
            }

            if (categoryId.HasValue)
            {
                query = query.Where(p => p.CategoryId == categoryId);
            }

            if (isFeatured.HasValue && isFeatured.Value)
            {
                query = query.Where(p => p.IsFeatured);
            }

            var items = await query.OrderBy(p => p.DisplayOrder).ToListAsync();

            return View(items);
        }

        // GET: CustomerPortfolioItem/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var item = await _context.PortfolioItems
                .Include(p => p.Artist)
                .ThenInclude(a => a.ArtistProfile)
                .Include(p => p.CategoryId)
                .Include(p => p.Portfolio)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (item == null)
            {
                return NotFound();
            }

            return View(item);
        }
    }
}
