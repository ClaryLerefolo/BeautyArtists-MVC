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
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier); // Use this for consistency and direct access
            if (userId == null)
            {
                return RedirectToPage("/Account/Login", new { area = "Identity" });
            }

            var portfolios = await _context.Portfolios
                .Where(p => p.ArtistId == userId)
                .OrderBy(p => p.Name) // Order for display
                .ToListAsync();

            ViewData["Title"] = "Manage Your Portfolios";
            return View(portfolios);
        }

        // GET: Portfolio/CreatePortfolio
        public IActionResult CreatePortfolio()
        {
            ViewData["Title"] = "Create New Portfolio";
            return View(new PortfolioCreateViewModel());
        }

        // POST: Portfolio/CreatePortfolio
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreatePortfolio(PortfolioCreateViewModel viewModel)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
            {
                return RedirectToPage("/Account/Login", new { area = "Identity" });
            }

            if (!ModelState.IsValid)
            {
                ViewData["Title"] = "Create New Portfolio";
                return View(viewModel);
            }

            var portfolio = new Portfolio
            {
                Name = viewModel.Name,
                Description = viewModel.Description,
                ArtistId = userId, // Use userId directly
                CreatedAt = DateTime.UtcNow // Use UtcNow for consistency
            };

            _context.Add(portfolio);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(ManagePortfolio));
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

        // GET: Portfolio/EditPortfolio/5
        public async Task<IActionResult> EditPortfolio(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
            {
                return RedirectToPage("/Account/Login", new { area = "Identity" });
            }

            var portfolio = await _context.Portfolios
                .FirstOrDefaultAsync(p => p.Id == id && p.ArtistId == userId);

            if (portfolio == null)
                return NotFound();

            var viewModel = new PortfolioEditViewModel
            {
                Id = portfolio.Id,
                Name = portfolio.Name,
                Description = portfolio.Description
            };

            ViewData["Title"] = $"Edit: {portfolio.Name}";
            return View(viewModel);
        }

        // POST: Portfolio/EditPortfolio/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPortfolio(int id, PortfolioEditViewModel viewModel)
        {
            if (id != viewModel.Id)
            {
                return NotFound();
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
            {
                return RedirectToPage("/Account/Login", new { area = "Identity" });
            }

            if (!ModelState.IsValid)
            {
                ViewData["Title"] = $"Edit: {viewModel.Name}";
                return View(viewModel);
            }

            var portfolio = await _context.Portfolios
                .FirstOrDefaultAsync(p => p.Id == id && p.ArtistId == userId);

            if (portfolio == null)
            {
                return NotFound();
            }

            portfolio.Name = viewModel.Name;
            portfolio.Description = viewModel.Description;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!PortfolioExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return RedirectToAction(nameof(ManagePortfolio));
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

    }
}