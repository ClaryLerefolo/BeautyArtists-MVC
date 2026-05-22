using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Models.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace BeautyArtists.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "Artist")]
    public class PortfolioItemController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _env;

        public PortfolioItemController(
            ApplicationDbContext context,
            IWebHostEnvironment env,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _env = env;
            _userManager = userManager;
        }

        // --------------------------------------------------------------------
        // LIST / FILTER
        // --------------------------------------------------------------------
        public async Task<IActionResult> ManagePortfolioItem(int? portfolioId, int? categoryId)
        {
            var artistId = _userManager.GetUserId(User);

            var items = _context.PortfolioItems
                .Include(p => p.CategoryId)
                .Include(p => p.Portfolio)
                .Where(p => p.ArtistId == artistId);

            if (portfolioId.HasValue)
                items = items.Where(p => p.PortfolioId == portfolioId);

            if (categoryId.HasValue)
                items = items.Where(p => p.CategoryId == categoryId);

            var categories = await _context.ServiceCategories
                .Select(c => new SelectListItem
                {
                    Value = c.Id.ToString(),
                    Text = c.Name,
                    Selected = categoryId.HasValue && c.Id == categoryId.Value
                }).ToListAsync();

            var portfolios = await _context.Portfolios
                .Where(p => p.ArtistId == artistId)
                .Select(p => new SelectListItem
                {
                    Value = p.Id.ToString(),
                    Text = p.Name,
                    Selected = portfolioId.HasValue && p.Id == portfolioId.Value
                }).ToListAsync();

            ViewBag.Categories = categories;
            ViewBag.Portfolios = portfolios;

            return View(await items.ToListAsync());
        }


        // --------------------------------------------------------------------
        // CREATE  (GET)
        // --------------------------------------------------------------------
        public async Task<IActionResult> CreatePortfolioItem()
        {
            var vm = new PortfolioItemViewModel
            {
                PortfoliosSelectList = await _context.Portfolios
                    .Select(p => new SelectListItem { Value = p.Id.ToString(), Text = p.Name })
                    .ToListAsync(),

                Categories = await _context.ServiceCategories
                    .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name })
                    .ToListAsync()
            };
            return View(vm);
        }

        // --------------------------------------------------------------------
        // CREATE  (POST)
        // --------------------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreatePortfolioItem(PortfolioItemViewModel vm)
        {
            var artistId = _userManager.GetUserId(User);

            if (!ModelState.IsValid)
            {
                // If it's an AJAX call from the modal, return JSON errors so the modal stays open
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return Json(new { success = false, errors = errors });
            }

            // Logic to save files
            string mediaUrl = await SaveFile(vm.MediaFile, "media");
            string? thumbnailUrl = vm.ThumbnailFile != null ? await SaveFile(vm.ThumbnailFile, "thumb") : null;

            var item = new PortfolioItem
            {
                Title = vm.Title,
                Description = vm.Description,
                Province = vm.Province,
                City = vm.City,
                DisplayOrder = vm.DisplayOrder,
                MediaType = vm.MediaType,
                MediaUrl = mediaUrl,
                ThumbnailUrl = thumbnailUrl,
                IsFeatured = vm.IsFeatured,
                PortfolioId = vm.PortfolioId,
                CategoryId = vm.CategoryId,
                ArtistId = artistId,
                UploadedAt = DateTime.UtcNow
            };

            _context.PortfolioItems.Add(item);
            await _context.SaveChangesAsync();

            // REDIRECT BACK TO MANAGEPORTFOLIO (Portfolio Controller), NOT ManagePortfolioItem
            return RedirectToAction("ManagePortfolio", "Portfolio");
        }

        // FIX FOR THE CRASHING SELECT:
        //   public async Task<IActionResult> ManagePortfolioItem(int? portfolioId)
        //   {
        //          var artistId = _userManager.GetUserId(User);
        //
        // var items = await _context.PortfolioItems
        //      .Include(p => p.Portfolio)
        //       .Include(p => p.Category) // FIX: Use Navigation Property, NOT CategoryId
        //     .Where(p => p.ArtistId == artistId)
        //    .ToListAsync();
        //
        // return View(items);
        //  }

        // --------------------------------------------------------------------
        // EDIT   (GET)
        // --------------------------------------------------------------------
        public async Task<IActionResult> EditPortfolioItem(int id)
        {
            var item = await _context.PortfolioItems.FindAsync(id);
            if (item == null) return NotFound();

            var vm = new PortfolioItemViewModel
            {
                Id = item.Id,
                Title = item.Title,
                Description = item.Description,
                CategoryId = item.CategoryId ?? 0,
                Province = item.Province,
                City = item.City,
                DisplayOrder = item.DisplayOrder,
                MediaType = item.MediaType,
                ExistingMediaUrl = item.MediaUrl,
                ExistingThumbnailUrl = item.ThumbnailUrl,
                IsFeatured = item.IsFeatured,
                PortfolioId = item.PortfolioId ?? 0
            };

            await LoadDropdowns(vm);
            return View(vm); // This will now render a full page view
        }

        // --------------------------------------------------------------------
        // EDIT (POST)
        // --------------------------------------------------------------------
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPortfolioItem(int id, PortfolioItemViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                await LoadDropdowns(vm);
                return View(vm);
            }

            var item = await _context.PortfolioItems.FindAsync(id);
            if (item == null) return NotFound();

            if (vm.MediaFile != null) item.MediaUrl = await SaveFile(vm.MediaFile, "media");
            if (vm.ThumbnailFile != null) item.ThumbnailUrl = await SaveFile(vm.ThumbnailFile, "thumb");

            item.Title = vm.Title;
            item.Description = vm.Description;
            item.Province = vm.Province;
            item.City = vm.City;
            item.DisplayOrder = vm.DisplayOrder;
            item.MediaType = vm.MediaType;
            item.IsFeatured = vm.IsFeatured;

            // Correctly handle the potential 0 values from dropdowns
            item.PortfolioId = vm.PortfolioId > 0 ? vm.PortfolioId : null;
            item.CategoryId = vm.CategoryId > 0 ? vm.CategoryId : null;

            await _context.SaveChangesAsync();

            return RedirectToAction("ManagePortfolio", "Portfolio");
        }

        // --------------------------------------------------------------------
        // DETAILS
        // --------------------------------------------------------------------
        public async Task<IActionResult> DetailsPortfolioItem(int id)
        {
            var item = await _context.PortfolioItems
                .Include(p => p.Portfolio)
                .Include(p => p.CategoryId)
                .FirstOrDefaultAsync(p => p.Id == id);

            return item is null ? NotFound() : View(item);
        }

        // --------------------------------------------------------------------
        // DELETE (GET)
        // --------------------------------------------------------------------
        // GET: PortfolioItem/DeletePortfolioItem/5
        public async Task<IActionResult> DeletePortfolioItem(int id)
        {
            var item = await _context.PortfolioItems
                .Include(p => p.Portfolio)
                .Include(p => p.Category) // FIXED: CategoryId was a bug
                .FirstOrDefaultAsync(p => p.Id == id);

            return item is null ? NotFound() : View(item);
        }

        // POST: PortfolioItem/DeleteItem/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteItem(int id)
        {
            var item = await _context.PortfolioItems.FindAsync(id);
            if (item == null) return Json(new { success = false, message = "Item not found" });

            _context.PortfolioItems.Remove(item);
            await _context.SaveChangesAsync();

            // This success response is what the JavaScript below waits for
            return Json(new { success = true });
        }

        // --------------------------------------------------------------------
        // DELETE (POST)
        // --------------------------------------------------------------------

        // --------------------------------------------------------------------
        // HELPERS
        // --------------------------------------------------------------------
        private async Task LoadDropdowns(PortfolioItemViewModel vm)
        {
            vm.PortfoliosSelectList = await _context.Portfolios
                .Select(p => new SelectListItem { Value = p.Id.ToString(), Text = p.Name })
                .ToListAsync();

            vm.Categories = await _context.ServiceCategories
                .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name })
                .ToListAsync();
        }

        private async Task<string> SaveFile(IFormFile file, string prefix)
        {
            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "portfolioitems");
            Directory.CreateDirectory(uploadsDir);

            var ext = Path.GetExtension(file.FileName);
            var fileName = $"{prefix}_{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);

            await using var fs = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(fs);

            return $"/uploads/portfolioitems/{fileName}";
        }

        // Remote validation: unique display order within the artist's items
        [AcceptVerbs("GET", "POST")]
        public async Task<IActionResult> IsDisplayOrderUnique(int displayOrder, int id)
        {
            var artistId = _userManager.GetUserId(User);
            var exists = await _context.PortfolioItems
                .AnyAsync(p => p.DisplayOrder == displayOrder &&
                               p.Id != id &&
                               p.ArtistId == artistId);

            return Json(!exists);
        }
    }
}
