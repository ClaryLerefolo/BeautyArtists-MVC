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
        // CREATE  (POST) - FIXED
        // --------------------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(int.MaxValue)]
        public async Task<IActionResult> CreatePortfolioItem(PortfolioItemViewModel vm)
        {
            var artistId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(artistId))
            {
                TempData["Error"] = "Session expired. Please log in again.";
                return RedirectToAction("ManagePortfolio", "Portfolio");
            }

            // 1. Validate file exists
            if (vm.MediaFile == null || vm.MediaFile.Length == 0)
            {
                TempData["Error"] = "Please select a file to upload.";
                return RedirectToAction("ManagePortfolio", "Portfolio");
            }

            // 2. Check file extension
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".mp4", ".mov", ".avi", ".webm", ".mkv" };
            var extension = Path.GetExtension(vm.MediaFile.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(extension))
            {
                TempData["Error"] = $"Unsupported file type '{extension}'. Allowed: JPG, PNG, GIF, MP4, MOV, AVI, WEBM.";
                return RedirectToAction("ManagePortfolio", "Portfolio");
            }

            // 3. Auto-detect MediaType from extension
            var videoExtensions = new[] { ".mp4", ".mov", ".avi", ".webm", ".mkv" };
            var mediaType = videoExtensions.Contains(extension) ? "Video" : "Image";

            // 4. Save file
            string mediaUrl;
            try
            {
                mediaUrl = await SaveFile(vm.MediaFile, "media");
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Failed to save file: {ex.Message}";
                return RedirectToAction("ManagePortfolio", "Portfolio");
            }

            // 5. Create the item
            var item = new PortfolioItem
            {
                Title = string.IsNullOrWhiteSpace(vm.Title) ? "Untitled" : vm.Title,
                Description = vm.Description,
           
                MediaUrl = mediaUrl,
                ThumbnailUrl = null, // You can generate one later
                IsFeatured = vm.IsFeatured,
                PortfolioId = vm.PortfolioId > 0 ? vm.PortfolioId : null,
                CategoryId = vm.CategoryId > 0 ? vm.CategoryId : null,
                ArtistId = artistId,
                UploadedAt = DateTime.UtcNow,
            };

            _context.PortfolioItems.Add(item);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"'{item.Title}' uploaded successfully!";
            return RedirectToAction("ManagePortfolio", "Portfolio");
        }

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
                DisplayOrder = item.DisplayOrder,
                ExistingMediaUrl = item.MediaUrl,
                ExistingThumbnailUrl = item.ThumbnailUrl,
                IsFeatured = item.IsFeatured,
                PortfolioId = item.PortfolioId ?? 0
            };

            await LoadDropdowns(vm);
            return View(vm);
        }

        // --------------------------------------------------------------------
        // EDIT (POST)
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPortfolioItem(int id, PortfolioItemViewModel vm)
        {
            // 1. Load the existing item
            var item = await _context.PortfolioItems.FindAsync(id);
            if (item == null) return NotFound();

            // 2. Remove validation for fields NOT in the edit form
            ModelState.Remove("Province");
            ModelState.Remove("City");
            ModelState.Remove("CategoryId");
            ModelState.Remove("PortfolioId");
            ModelState.Remove("MediaType"); // MediaType is set when a new file is uploaded

            // 3. If ModelState is still invalid (Title, Description, etc.), return errors
            if (!ModelState.IsValid)
            {
                await LoadDropdowns(vm);
                return View(vm);
            }

            // 4. Update ONLY the fields that are present in the form
            item.Title = vm.Title ?? item.Title;
            item.Description = vm.Description ?? item.Description;
            item.DisplayOrder = vm.DisplayOrder;
            item.IsFeatured = vm.IsFeatured;

            // 5. Handle file upload if a new file is provided
            if (vm.MediaFile != null && vm.MediaFile.Length > 0)
            {
                // Delete old file
                if (!string.IsNullOrEmpty(item.MediaUrl))
                {
                    var oldPath = Path.Combine(_env.WebRootPath, item.MediaUrl.TrimStart('/'));
                    if (System.IO.File.Exists(oldPath))
                        System.IO.File.Delete(oldPath);
                }

                // Save new file
                item.MediaUrl = await SaveFile(vm.MediaFile, "media");

                // Auto-detect media type from extension
                var ext = Path.GetExtension(vm.MediaFile.FileName).ToLowerInvariant();
                var videoExtensions = new[] { ".mp4", ".mov", ".avi", ".webm", ".mkv" };
                item.MediaType = videoExtensions.Contains(ext) ? "Video" : "Image";
            }

            // 6. Keep Province, City, CategoryId, PortfolioId unchanged (not updated)
            //    They are NOT in the form, so we DON'T assign them from vm.

            // 7. Save changes
            await _context.SaveChangesAsync();

            TempData["Success"] = $"'{item.Title}' updated successfully!";
            return RedirectToAction("ManageServices", "Artist"); // change to "ManageServices" if you want
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

        // POST: PortfolioItem/DeleteItem/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteItem(int id)
        {
            var item = await _context.PortfolioItems.FindAsync(id);
            if (item == null) return Json(new { success = false, message = "Item not found" });

            // Delete the file
            if (!string.IsNullOrEmpty(item.MediaUrl))
            {
                var filePath = Path.Combine(_env.WebRootPath, item.MediaUrl.TrimStart('/'));
                if (System.IO.File.Exists(filePath))
                    System.IO.File.Delete(filePath);
            }
            if (!string.IsNullOrEmpty(item.ThumbnailUrl))
            {
                var thumbPath = Path.Combine(_env.WebRootPath, item.ThumbnailUrl.TrimStart('/'));
                if (System.IO.File.Exists(thumbPath))
                    System.IO.File.Delete(thumbPath);
            }

            _context.PortfolioItems.Remove(item);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

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
            if (!Directory.Exists(uploadsDir))
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