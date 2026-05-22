using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Models.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace BeautyArtists.Controllers
{
    public class ServicesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public ServicesController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }


        // GET: Services
        // GET: Services/Create (not a partial, just fills ViewBag for the main page)
        // 1. LANDING PAGE: List all services
        public async Task<IActionResult> Index()
        {
            var services = await _context.Services
                .Include(s => s.ServiceCategory) // Matches your Model property name
                .OrderBy(s => s.Name)
                .ToListAsync();

            return View(services); // Returns List<Service>
        }

        // 2. CREATE PAGE: Show the blank form
        public async Task<IActionResult> Create()
        {
            var categoryList = await _context.ServiceCategories
                .OrderBy(c => c.Name)
                .Select(c => new SelectListItem
                {
                    Value = c.Id.ToString(),
                    Text = c.Name
                }).ToListAsync();

            // Fill the ViewBag so the View can find it easily
            ViewBag.Categories = categoryList;

            var viewModel = new ServiceViewModel
            {
                Categories = categoryList
            };

            return View(viewModel);
        }
        // CREATE (AJAX)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ServiceViewModel model)
        {
            if (!ModelState.IsValid)
            {
                // If validation fails, we must reload the categories for the dropdown
                model.Categories = await _context.ServiceCategories
                    .Select(c => new SelectListItem
                    {
                        Value = c.Id.ToString(),
                        Text = c.Name
                    }).ToListAsync();
                return View(model);
            }

            var newService = new Service
            {
                Name = model.Name,
                Description = model.Description ?? "",
                BasePrice = model.BasePrice,
                CategoryId = model.CategoryId,
                Duration = model.Duration,
                CreatedAt = DateTime.UtcNow
            };

            _context.Services.Add(newService);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index)); // Go back to the list after saving
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var service = await _context.UserServices.FindAsync(id);
            if (service == null) return NotFound();
            ViewBag.ServiceId = new SelectList(_context.Services, "Id", "Name", service.ServiceId);
            return View(service);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(UserService model, IFormFile ImageFile)
        {
            var us = await _context.UserServices.FindAsync(model.Id);
            if (us == null)
                return Json(new { success = false, message = "Not found" });

            // Always assign ArtistId from logged-in user
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Json(new { success = false, message = "Not logged in" });

            us.ArtistId = userId; // ✅ fixes "ArtistId required"

            // Remove validation for navigation properties (Artist, Service) 
            // and revalidate the model
            ModelState.Remove(nameof(model.Artist));
            ModelState.Remove(nameof(model.Service));

            if (!ModelState.IsValid)
            {
                return Json(new
                {
                    success = false,
                    errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)
                });
            }

            // Update editable fields
            us.ServiceId = model.ServiceId; // ✅ fixes "ServiceId required"
            us.Price = model.Price;
            us.Duration = model.Duration;
            us.CustomDescription = model.CustomDescription;
            us.IsActive = model.IsActive;

            if (ImageFile != null && ImageFile.Length > 0)
            {
                Directory.CreateDirectory("wwwroot/uploads/services");
                var fileName = $"{Guid.NewGuid()}_{Path.GetFileName(ImageFile.FileName)}";
                var filePath = Path.Combine("wwwroot/uploads/services", fileName);
                using var stream = new FileStream(filePath, FileMode.Create);
                await ImageFile.CopyToAsync(stream);
                us.ImagePath = "/uploads/services/" + fileName;
            }

            await _context.SaveChangesAsync();

            var svc = await _context.Services
                .Include(s => s.ServiceCategory)
                .FirstOrDefaultAsync(s => s.Id == us.ServiceId);

            return Json(new
            {
                success = true,
                service = new
                {
                    id = us.Id,
                    name = svc?.Name ?? "",
                    category = svc?.ServiceCategory?.Name ?? "",
                    basePrice = svc?.BasePrice ?? 0,
                    serviceDescription = svc?.Description ?? "",
                    price = us.Price,
                    duration = us.Duration,
                    customDescription = us.CustomDescription ?? "",
                    isActive = us.IsActive,
                    imagePath = us.ImagePath
                }
            });
        }


        // DELETE (AJAX)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var service = await _context.Services.FindAsync(id);
            if (service == null) return Json(new { success = false, message = "Not found" });

            _context.Services.Remove(service);
            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }
    }
}
