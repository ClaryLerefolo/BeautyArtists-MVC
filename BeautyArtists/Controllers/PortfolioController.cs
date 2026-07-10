using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Models.ViewModels; // Ensure this namespace is correct for your ViewModels
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering; // Added for SelectListItem
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Claims; // Added for User.FindFirstValue

namespace BeautyArtists.Controllers
{
    [Authorize(Roles = "Artist")]
    public class PortfolioController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _hostEnvironment; // Added for file uploads

        public PortfolioController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment hostEnvironment) // Added to constructor
        {
            _context = context;
            _userManager = userManager;
            _hostEnvironment = hostEnvironment; // Initialize
        }

        // --- Portfolio (Collections) Management Actions ---

        // GET: Portfolio/ManagePortfolio
        // This action lists the artist's Portfolios (e.g., "Wedding Portfolio", "Commercial Work")
        public async Task<IActionResult> ManagePortfolio()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return RedirectToPage("/Account/Login", new { area = "Identity" });
            }

            // 1. Load Categories so the "Add Item" modal dropdown actually has data
            ViewBag.Categories = await _context.ServiceCategories
                .Select(c => new SelectListItem
                {
                    Value = c.Id.ToString(),
                    Text = c.Name
                }).ToListAsync();

            // 2. Get Portfolios + Items + Categories for the items
            var portfolios = await _context.Portfolios
                .Include(p => p.Items)
                    .ThenInclude(i => i.Category)
                .Where(p => p.ArtistId == userId)
                .OrderByDescending(p => p.CreatedAt) // Newest portfolios first
                .ToListAsync();

            return View(portfolios);
        }

        // GET: Portfolio/CreatePortfolio
        public IActionResult CreatePortfolio()
        {
            ViewData["Title"] = "Create New Portfolio";
            return View(new PortfolioCreateViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreatePortfolio(PortfolioCreateViewModel viewModel)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (userId == null)
            {
                return Json(new { success = false, message = "Session expired. Please login." });
            }

            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return Json(new { success = false, message = "Validation failed", errors = errors });
            }

            try
            {
                var portfolio = new Portfolio
                {
                    Name = viewModel.Name,
                    Description = viewModel.Description,
                    ArtistId = userId,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Add(portfolio);
                await _context.SaveChangesAsync();

                // THIS IS THE KEY: Return JSON only for the AJAX call
                return Json(new
                {
                    success = true,
                    portfolio = new
                    {
                        id = portfolio.Id,
                        name = portfolio.Name,
                        description = portfolio.Description,
                        createdAt = portfolio.CreatedAt.ToString("yyyy-MM-dd")
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Database error: " + ex.Message });
            }
        }
        // POST: CreatePortfolioItem (Upload Logic)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(int.MaxValue)]
        public async Task<IActionResult> CreatePortfolioItem(PortfolioItemViewModel vm)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                TempData["Error"] = "Session expired. Please log in again.";
                return RedirectToAction(nameof(ManagePortfolio));
            }

            // 1. Validate file exists
            if (vm.MediaFile == null || vm.MediaFile.Length == 0)
            {
                TempData["Error"] = "Please select a file to upload.";
                return RedirectToAction(nameof(ManagePortfolio));
            }

            // 2. Check file extension
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".mp4", ".mov", ".avi", ".webm", ".mkv" };
            var extension = Path.GetExtension(vm.MediaFile.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(extension))
            {
                TempData["Error"] = $"Unsupported file type '{extension}'. Allowed: JPG, PNG, GIF, MP4, MOV, AVI, WEBM.";
                return RedirectToAction(nameof(ManagePortfolio));
            }

            // 3. Auto-detect MediaType from extension
            var videoExtensions = new[] { ".mp4", ".mov", ".avi", ".webm", ".mkv" };
            var mediaType = videoExtensions.Contains(extension) ? "Video" : "Image";

            // 4. Save the file
            string mediaUrl;
            try
            {
                mediaUrl = await SaveFile(vm.MediaFile, "media");
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Failed to save file: {ex.Message}";
                return RedirectToAction(nameof(ManagePortfolio));
            }

            // 5. Create the item with MediaType set correctly
            var item = new PortfolioItem
            {
                Title = string.IsNullOrWhiteSpace(vm.Title) ? "Untitled" : vm.Title,
                Description = vm.Description ?? "",
                Province = vm.Province ?? "",
                City = vm.City ?? "",
                DisplayOrder = vm.DisplayOrder,
                MediaType = mediaType, // CRITICAL: "Video" or "Image"
                MediaUrl = mediaUrl,
                ThumbnailUrl = null,
                IsFeatured = vm.IsFeatured,
                PortfolioId = vm.PortfolioId > 0 ? vm.PortfolioId : null,
                CategoryId = vm.CategoryId > 0 ? vm.CategoryId : null,
                ArtistId = userId,
                UploadedAt = DateTime.UtcNow,
                ClientName = vm.ClientName,
                MusicTrack = vm.MusicTrack,
                UserServiceId = vm.UserServiceId > 0 ? vm.UserServiceId : null,
            };

            _context.PortfolioItems.Add(item);
            await _context.SaveChangesAsync();

            // Verify the item was saved correctly
            var savedItem = await _context.PortfolioItems.FindAsync(item.Id);
            TempData["Success"] = $"'{item.Title}' uploaded successfully! MediaType: {savedItem?.MediaType}";
            return RedirectToAction("ManageServices", "Artist");   // ← changed from ManagePortfolio
        }

        // Helper Method for Uploads
        private async Task<string> SaveFile(IFormFile file, string prefix)
        {
            var uploadsDir = Path.Combine(_hostEnvironment.WebRootPath, "uploads", "portfolioitems");
            if (!Directory.Exists(uploadsDir)) Directory.CreateDirectory(uploadsDir);

            var ext = Path.GetExtension(file.FileName);
            var fileName = $"{prefix}_{Guid.NewGuid():N}{ext}";
            var filePath = Path.Combine(uploadsDir, fileName);

            using (var fs = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fs);
            }
            return $"/uploads/portfolioitems/{fileName}";
        }
    

// GET: Portfolio/DetailsPortfolio/5
// Shows details of a specific Portfolio AND its associated PortfolioItems
public async Task<IActionResult> DetailsPortfolio(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
            {
                return RedirectToPage("/Account/Login", new { area = "Identity" });
            }

            var portfolio = await _context.Portfolios
                .Include(p => p.Items.OrderBy(pi => pi.DisplayOrder)) // Eager load and order items
                .FirstOrDefaultAsync(p => p.Id == id && p.ArtistId == userId);

            if (portfolio == null)
                return NotFound();

            var viewModel = new PortfolioDetailsViewModel
            {
                Id = portfolio.Id,
                Name = portfolio.Name,
                Description = portfolio.Description,
                CreatedAt = portfolio.CreatedAt,
                Items = portfolio.Items
            };

            ViewData["Title"] = $"Details: {portfolio.Name}";
            return View(viewModel);
        }

        // POST: Portfolio/EditPortfolio/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPortfolio(int id, PortfolioEditViewModel viewModel)
        {
            // 1. Check if the ID from the URL matches the Hidden Input in the Form
            if (id != viewModel.Id)
            {
                return Json(new { success = false, message = $"ID Mismatch: URL has {id}, Form has {viewModel.Id}" });
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // 2. Find the actual database record
            var portfolio = await _context.Portfolios
                .FirstOrDefaultAsync(p => p.Id == id && p.ArtistId == userId);

            if (portfolio == null)
            {
                return Json(new { success = false, message = "Portfolio not found or you don't own it." });
            }

            // 3. MANUAL VALIDATION (Bypass the ViewModel's [Required] drama if needed)
            if (string.IsNullOrWhiteSpace(viewModel.Name))
            {
                return Json(new { success = false, message = "Name is required." });
            }

            // 4. Update the DB object with the values from the form
            portfolio.Name = viewModel.Name;
            portfolio.Description = viewModel.Description; // Even if this is null/empty, it will now save

            try
            {
                _context.Portfolios.Update(portfolio);
                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Database Error: " + ex.Message });
            }
        }

        // POST: Portfolio/DeletePortfolio/5
        [HttpPost, ActionName("DeletePortfolio")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePortfolioConfirmed(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
            {
                return RedirectToPage("/Account/Login", new { area = "Identity" });
            }

            var portfolio = await _context.Portfolios
                .Include(p => p.Items) // Include items to handle cascading delete or manual file cleanup
                .FirstOrDefaultAsync(p => p.Id == id && p.ArtistId == userId);

            if (portfolio == null)
                return NotFound();

            // Optional: If you want to delete associated PortfolioItem files when a Portfolio is deleted
            // This assumes your database is NOT configured for cascading deletes on PortfolioItems.
            // If it IS configured for cascading deletes, the items will be removed from DB automatically.
            // But files still need manual deletion.
            foreach (var item in portfolio.Items)
            {
                if (!string.IsNullOrEmpty(item.MediaUrl))
                {
                    string filePath = Path.Combine(_hostEnvironment.WebRootPath, item.MediaUrl.TrimStart('/'));
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }
                }
                if (!string.IsNullOrEmpty(item.ThumbnailUrl))
                {
                    string thumbnailPath = Path.Combine(_hostEnvironment.WebRootPath, item.ThumbnailUrl.TrimStart('/'));
                    if (System.IO.File.Exists(thumbnailPath))
                    {
                        System.IO.File.Delete(thumbnailPath);
                    }
                }
            }
            // End optional file cleanup

            _context.Portfolios.Remove(portfolio);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(ManagePortfolio));
        }

        private bool PortfolioExists(int id)
        {
            return _context.Portfolios.Any(e => e.Id == id);
        }

        [AllowAnonymous]
        public async Task<IActionResult> ArtistServicePortfolio(string artistId, int serviceId)
        {
            if (string.IsNullOrEmpty(artistId) || serviceId == 0)
                return NotFound();

            // Get the service and artist details
            var userService = await _context.UserServices
                .Include(us => us.Service)
                .Include(us => us.Artist)
                    .ThenInclude(a => a.ArtistProfile)
                .FirstOrDefaultAsync(us => us.Id == serviceId && us.ArtistId == artistId);
            if (userService == null)
                return NotFound();

            var categoryId = userService.Service?.CategoryId ?? 0;

            // Get portfolios that have items in this category for this artist
            var portfolios = await _context.Portfolios
                .Include(p => p.Items.Where(i => i.CategoryId == categoryId))
                .Include(p => p.Artist)
                .Where(p => p.ArtistId == artistId
                         && p.Items.Any(i => i.CategoryId == categoryId))
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();

            var vm = new ArtistServicePortfolioViewModel
            {
                ArtistId = artistId,
                ArtistName = !string.IsNullOrEmpty(userService.Artist?.FirstName)
                    ? $"{userService.Artist.FirstName} {userService.Artist.LastName}".Trim()
                    : userService.Artist?.UserName ?? "Pro Artist",
                ArtistProfilePicture = userService.Artist?.ArtistProfile?.ProfilePictureUrl ?? "/images/default-profile.png",
                ServiceName = userService.Service?.Name ?? "Service",
                ServiceDescription = userService.CustomDescription ?? userService.Service?.Description ?? "",
                ServicePrice = userService.Price,
                UserServiceId = userService.Id,
                Portfolios = portfolios
            };

            return View(vm);
        }
        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> ArtistPortfolioModal(string artistId, int serviceId)
        {
            if (string.IsNullOrEmpty(artistId))
                return PartialView("_ArtistPortfolioModal", null);

            var userService = await _context.UserServices
                .Include(us => us.Service)
                .Include(us => us.Artist)
                .FirstOrDefaultAsync(us => us.Id == serviceId);

            var categoryId = userService?.Service?.CategoryId ?? 0; // NEW

            var portfolios = await _context.Portfolios
                .Include(p => p.Items.Where(i => i.CategoryId == categoryId))
                .Include(p => p.Artist)
                .Where(p => p.ArtistId == artistId
                         && p.Items.Any(i => i.CategoryId == categoryId))
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
       

            var vm = new ArtistPortfolioModalViewModel
            {
                ArtistId = artistId,
                ArtistName = !string.IsNullOrEmpty(userService?.Artist?.FirstName)
                    ? $"{userService.Artist.FirstName} {userService.Artist.LastName}".Trim()
                    : userService?.Artist?.UserName ?? "Pro Artist",
                ServiceName = userService?.Service?.Name ?? "",
                Portfolios = portfolios
            };

            return PartialView("_ArtistPortfolioModal", vm);
        }

    }
}