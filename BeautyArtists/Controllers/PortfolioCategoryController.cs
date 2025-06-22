using BeautyArtists.Data;
using BeautyArtists.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeautyArtists.Controllers
{
    [Authorize(Roles = "Admin")]
    public class PortfolioCategoryController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PortfolioCategoryController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> ManageCategory()
        {
            var categories = await _context.PortfolioCategories.ToListAsync();
            return View(categories);
        }

        public IActionResult CreateCategory()
        { 
            return View();
        } 

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCategory(PortfolioCategory category)
        {
            if (!ModelState.IsValid)
                return View(category);

            _context.Add(category);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(ManageCategory));
        }

        public async Task<IActionResult> EditCategory(int id)
        {
            var category = await _context.PortfolioCategories.FindAsync(id);
            if (category == null) return NotFound();
            return View(category);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditCategory(PortfolioCategory category)
        {
            if (!ModelState.IsValid)
                return View(category);

            _context.Update(category);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(ManageCategory));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCategory(int id)
        {
            var category = await _context.PortfolioCategories.FindAsync(id);
            if (category != null)
            {
                _context.Remove(category);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(ManageCategory));
        }
    }
}
