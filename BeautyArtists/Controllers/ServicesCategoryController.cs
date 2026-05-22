using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

public class ServiceCategoryController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly string _uploadFolder;

    public ServiceCategoryController(ApplicationDbContext context)
    {
        _context = context;
        _uploadFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "categories");
        Directory.CreateDirectory(_uploadFolder);
    }

    public async Task<IActionResult> ManageCategory()
    {
        var categories = await _context.ServiceCategories.ToListAsync();
        return View(categories);
    }

    public IActionResult CreateCategory()
    {
        return View(new ServiceCategoryViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCategory(ServiceCategoryViewModel viewModel)
    {
        if (!ModelState.IsValid)
            return View(viewModel);

        string imagePath = "/images/category-default.jpg"; // safer fallback  

        if (viewModel.ImageFile != null && viewModel.ImageFile.Length > 0)
        {
            var uniqueFileName = Guid.NewGuid() + Path.GetExtension(viewModel.ImageFile.FileName);
            var filePath = Path.Combine(_uploadFolder, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await viewModel.ImageFile.CopyToAsync(stream);
            }

            imagePath = "/uploads/categories/" + uniqueFileName;
        }

        var category = new ServiceCategory
        {
            Name = viewModel.Name,
            IconName = viewModel.IconName,
            CoverImagePath = imagePath
        };

        _context.ServiceCategories.Add(category);
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(ManageCategory));
    }

    // GET: EditCategory
    public async Task<IActionResult> EditCategory(int? id)
    {
        if (id == null) return NotFound();

        var category = await _context.ServiceCategories.FindAsync(id);
        if (category == null) return NotFound();

        var viewModel = new ServiceCategoryViewModel
        {
            Id = category.Id,
            Name = category.Name,
            IconName = category.IconName,
            ExistingImagePath = category.CoverImagePath
        };

        return View(viewModel);
    }

    // POST: EditCategory
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCategory(int id, ServiceCategoryViewModel viewModel)
    {
        if (id != viewModel.Id) return NotFound();
        if (!ModelState.IsValid) return View(viewModel);

        var category = await _context.ServiceCategories.FindAsync(id);
        if (category == null) return NotFound();

        category.Name = viewModel.Name;
        category.IconName = viewModel.IconName;

        if (viewModel.ImageFile != null && viewModel.ImageFile.Length > 0)
        {
            // Optional: delete old image (except default)
            if (!string.IsNullOrEmpty(category.CoverImagePath) &&
                category.CoverImagePath.StartsWith("/uploads/") &&
                category.CoverImagePath != "/images/category-default.jpg")
            {
                var oldPath = Path.Combine("wwwroot", category.CoverImagePath.TrimStart('/'));
                if (System.IO.File.Exists(oldPath))
                    System.IO.File.Delete(oldPath);
            }

            var uniqueFileName = Guid.NewGuid() + Path.GetExtension(viewModel.ImageFile.FileName);
            var filePath = Path.Combine(_uploadFolder, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await viewModel.ImageFile.CopyToAsync(stream);
            }

            category.CoverImagePath = "/uploads/categories/" + uniqueFileName;
        }

        _context.Update(category);
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(ManageCategory));
    }



    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var category = await _context.ServiceCategories.FindAsync(id);

        if (category != null)
        {
            // Optional: check if category is linked to portfolios/items
            // and handle gracefully instead of throwing SQL FK error.
            var hasItems = await _context.PortfolioItems.AnyAsync(p => p.CategoryId == id);
            if (hasItems)
            {
                TempData["Error"] = "Cannot delete this category because it is linked to existing portfolio items.";
                return RedirectToAction(nameof(ManageCategory));
            }

            _context.ServiceCategories.Remove(category);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Category deleted successfully.";
        }

        return RedirectToAction(nameof(ManageCategory));
    }

}
