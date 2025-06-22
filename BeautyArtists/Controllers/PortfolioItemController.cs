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
                .Include(p => p.Category)
                .Include(p => p.Portfolio)
                .Where(p => p.ArtistId == artistId);

            if (portfolioId.HasValue)
                items = items.Where(p => p.PortfolioId == portfolioId);

            if (categoryId.HasValue)
                items = items.Where(p => p.CategoryId == categoryId);

            var categories = await _context.PortfolioCategories
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

                Categories = await _context.PortfolioCategories
                    .Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.Name })
                    .ToListAsync()
            };
            return View(vm);
        }

        // --------------------------------------------------------------------
        // CREATE  (POST)
        // --------------------------------------------------------------------
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CreatePortfolioItem(PortfolioItemViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                await LoadDropdowns(vm);
                return View(vm);
            }

            string mediaUrl = await SaveFile(vm.MediaFile, "media");
            string? thumbnailUrl = vm.ThumbnailFile != null
                                        ? await SaveFile(vm.ThumbnailFile, "thumb")
                                        : null;

            var artistId = _userManager.GetUserId(User);
            if (artistId is null)
                return Unauthorized();

            var item = new PortfolioItem
            {
                Title = vm.Title,
                Description = vm.Description,
                CategoryId = vm.CategoryId,
                Location = vm.Location,
                ClientName = vm.ClientName,
                MusicTrack = vm.MusicTrack,
                DisplayOrder = vm.DisplayOrder,
                MediaType = vm.MediaType,
                MediaUrl = mediaUrl,
                ThumbnailUrl = thumbnailUrl,
                IsFeatured = vm.IsFeatured,
                PortfolioId = vm.PortfolioId,
                UploadedAt = DateTime.UtcNow,
                ArtistId = artistId
            };

            _context.PortfolioItems.Add(item);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(ManagePortfolioItem));
        }

        // --------------------------------------------------------------------
        // EDIT   (GET)
        // --------------------------------------------------------------------
        public async Task<IActionResult> EditPortfolioItem(int id)
        {
            var item = await _context.PortfolioItems.FindAsync(id);
            if (item is null) return NotFound();

            var vm = new PortfolioItemViewModel
            {
                Id = item.Id,
                Title = item.Title,
                Description = item.Description,
                CategoryId = item.CategoryId ?? 0,
                Location = item.Location,
                ClientName = item.ClientName,
                MusicTrack = item.MusicTrack,
                DisplayOrder = item.DisplayOrder,
                MediaType = item.MediaType,
                ExistingMediaUrl = item.MediaUrl,
                ExistingThumbnailUrl = item.ThumbnailUrl,
                IsFeatured = item.IsFeatured,
                PortfolioId = item.PortfolioId ?? 0
            };

            await LoadDropdowns(vm);
            return View(vm);
        }

        // --------------------------------------------------------------------
        // EDIT   (POST)
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
            if (item is null) return NotFound();

            if (vm.MediaFile != null) item.MediaUrl = await SaveFile(vm.MediaFile, "media");
            if (vm.ThumbnailFile != null) item.ThumbnailUrl = await SaveFile(vm.ThumbnailFile, "thumb");

            item.Title = vm.Title;
            item.Description = vm.Description;
            item.CategoryId = vm.CategoryId;
            item.Location = vm.Location;
            item.ClientName = vm.ClientName;
            item.MusicTrack = vm.MusicTrack;
            item.DisplayOrder = vm.DisplayOrder;
            item.MediaType = vm.MediaType;
            item.IsFeatured = vm.IsFeatured;
            item.PortfolioId = vm.PortfolioId;

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(ManagePortfolioItem));
        }

        // --------------------------------------------------------------------
        // DETAILS
        // --------------------------------------------------------------------
        public async Task<IActionResult> DetailsPortfolioItem(int id)
        {
            var item = await _context.PortfolioItems
                .Include(p => p.Portfolio)
                .Include(p => p.Category)
                .FirstOrDefaultAsync(p => p.Id == id);

            return item is null ? NotFound() : View(item);
        }

        // --------------------------------------------------------------------
        // DELETE (GET)
        // --------------------------------------------------------------------
        public async Task<IActionResult> DeletePortfolioItem(int id)
        {
            var item = await _context.PortfolioItems
                .Include(p => p.Portfolio)
                .Include(p => p.Category)
                .FirstOrDefaultAsync(p => p.Id == id);

            return item is null ? NotFound() : View(item);
        }

        // --------------------------------------------------------------------
        // DELETE (POST)
        // --------------------------------------------------------------------
        [HttpPost, ActionName("DeletePortfolioItem"), ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var item = await _context.PortfolioItems.FindAsync(id);
            if (item is null) return NotFound();

            _context.PortfolioItems.Remove(item);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(ManagePortfolioItem));
        }

        // --------------------------------------------------------------------
        // HELPERS
        // --------------------------------------------------------------------
        private async Task LoadDropdowns(PortfolioItemViewModel vm)
        {
            vm.PortfoliosSelectList = await _context.Portfolios
                .Select(p => new SelectListItem { Value = p.Id.ToString(), Text = p.Name })
                .ToListAsync();

            vm.Categories = await _context.PortfolioCategories
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
