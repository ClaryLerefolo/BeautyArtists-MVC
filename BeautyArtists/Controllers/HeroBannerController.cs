using BeautyArtists.Data;
using BeautyArtists.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeautyArtists.Controllers
{
    public class HeroBannersController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HeroBannersController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: HeroBanners (Admin List)
        public async Task<IActionResult> Index()
        {
            return View(await _context.HeroBanners.ToListAsync());
        }

        // GET: HeroBanners/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: HeroBanners/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(HeroBanner banner, IFormFile imageFile)
        {
            if (ModelState.IsValid)
            {
                if (imageFile != null && imageFile.Length > 0)
                {
                    var fileName = Guid.NewGuid().ToString() + Path.GetExtension(imageFile.FileName);
                    var filePath = Path.Combine("wwwroot/uploads/hero", fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await imageFile.CopyToAsync(stream);
                    }

                    banner.ImagePath = "/uploads/hero/" + fileName;
                }

                _context.Add(banner);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(banner);
        }

        // Edit + Delete actions similar (I can give full code if you want)
    }
}
