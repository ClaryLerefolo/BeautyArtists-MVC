using BeautyArtists.Data;
using BeautyArtists.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeautyArtists.Controllers
{
    public class ServicesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ServicesController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string category)
        {
            var categories = await _context.UserServices
                .Include(us => us.Service)
                    .ThenInclude(s => s.Category)
                .Where(us => us.IsActive && us.Service.Category != null)
                .Select(us => us.Service.Category.Name)
                .Distinct()
                .OrderBy(name => name)
                .ToListAsync();

            ViewBag.Categories = categories;
            ViewBag.SelectedCategory = category;

            var services = _context.UserServices
                .Include(us => us.Service)
                    .ThenInclude(s => s.Category)
                .Include(us => us.Artist)
                .Where(us => us.IsActive)
                .AsQueryable();

            if (!string.IsNullOrEmpty(category))
            {
                services = services.Where(us => us.Service.Category.Name == category);
            }

            return View(await services.ToListAsync());
        }


        public async Task<IActionResult> Details(int id)
        {
            var service = await _context.UserServices
                .Include(us => us.Service)
                    .ThenInclude(s => s.Category)
                .Include(us => us.Artist)
                    .ThenInclude(a => a.ArtistProfile)
                .FirstOrDefaultAsync(us => us.Id == id);

            if (service == null)
                return NotFound();

            return View(service);
        }
    }
}
