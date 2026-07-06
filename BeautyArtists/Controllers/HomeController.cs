using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Build.Framework;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace BeautyArtists.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;


        public HomeController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }


        public async Task<IActionResult> Index()
        {
            var banners = await _context.HeroBanners.ToListAsync();
            var categories = await _context.ServiceCategories.ToListAsync();
            var testimonials = await _context.Testimonials.ToListAsync();

            // ONE service per artist — take the newest active one
            var featuredServices = await _context.UserServices
                .Include(us => us.Service)
                .Include(us => us.Artist)
                .Where(us => us.IsActive)
                .GroupBy(us => us.ArtistId)          // group by artist
                .Select(g => g.OrderByDescending(us => us.Id).First()) // newest per artist
                .Take(6)
                .ToListAsync();

            // ?? TOP RATED SERVICES (min 3 reviews, ordered by rating)
            // ?? TOP RATED SERVICES (via Bookings)
            var topRatedServices = await _context.UserServices
                .Include(us => us.Service)
                .Include(us => us.Artist)
                .Where(us => us.IsActive)
                .Select(us => new
                {
                    Service = us,
                    AverageRating = _context.Reviews
                        .Where(r => r.Booking.UserServiceId == us.Id)
                        .Average(r => (double?)r.Rating) ?? 0,
                    ReviewCount = _context.Reviews
                        .Where(r => r.Booking.UserServiceId == us.Id)
                        .Count()
                })
                .Where(x => x.ReviewCount >= 3)
                .OrderByDescending(x => x.AverageRating)
                .ThenByDescending(x => x.ReviewCount)
                .Take(6)
                .Select(x => new TopRatedService
                {
                    Service = x.Service,
                    AverageRating = x.AverageRating,
                    ReviewCount = x.ReviewCount
                })
                .ToListAsync();

            var model = new HomeViewModel
            {
                Banners = banners,
                Categories = categories,
                FeaturedServices = featuredServices,
                Testimonials = testimonials,
                TopRatedServices = topRatedServices

            };
            return View(model);
        }
        public async Task<IActionResult> ViewService(string artistId)
        {

            // Add this BEFORE building the model
            ViewBag.Portfolios = await _context.PortfolioItems.ToListAsync();
            if (string.IsNullOrEmpty(artistId)) return NotFound();

            var artist = await _context.Users
                .Include(u => u.ArtistProfile)
                .FirstOrDefaultAsync(u => u.Id == artistId);

            if (artist == null) return NotFound();

            var userServices = await _context.UserServices
                .Where(us => us.ArtistId == artistId && us.IsActive)
                .Include(us => us.Service)
                    .ThenInclude(s => s.ServiceCategory)
                .Include(us => us.Artist)
                .ToListAsync();

            ViewBag.Portfolios = await _context.Portfolios
                .Include(p => p.Items)
                .Where(p => p.ArtistId == artistId)
                .ToListAsync();

            // GROUP by category — one representative service per category
            var groupedServices = userServices
                .GroupBy(us => us.Service?.ServiceCategory?.Name ?? "Other")
                .Select(g => g.First()) // one per category
                .ToList();

            var model = new ServiceListViewModel
            {
                Title = $"{(!string.IsNullOrEmpty(artist.FirstName) ? $"{artist.FirstName} {artist.LastName}".Trim() : artist.UserName)}'s Services",
                ArtistId = artist.Id,
                ArtistName = !string.IsNullOrEmpty(artist.FirstName)
                    ? $"{artist.FirstName} {artist.LastName}".Trim()
                    : artist.UserName ?? artist.Email,
                ArtistLocation = !string.IsNullOrEmpty(artist.ArtistProfile?.City)
    ? $"{artist.ArtistProfile.City}, {artist.ArtistProfile.Province}"
    : artist.ArtistProfile?.Province ?? "",
                ArtistProfilePicture = artist.ArtistProfile?.ProfilePictureUrl
                                       ?? "/images/default-profile.png",
                Services = groupedServices.Select(us => new ServiceListViewModel.ServiceItem
                {
                    UserServiceId = us.Id,
                    ServiceName = us.Service?.ServiceCategory?.Name ?? us.Service?.Name ?? "No Name",
                    Description = us.CustomDescription ?? us.Service?.Description ?? "",
                    Category = us.Service?.ServiceCategory?.Name ?? "Uncategorized",
                    CategoryId = us.Service?.CategoryId ?? 0,
                    Price = us.Price,
                    ImagePath = us.ImagePath ?? us.Service?.ImagePath,
                    ArtistName = !string.IsNullOrEmpty(artist.FirstName)
                        ? $"{artist.FirstName} {artist.LastName}".Trim()
                        : artist.UserName ?? "Pro Artist",
                    ArtistId = us.ArtistId,
                }).ToList()
            };

            return View("ServiceList", model);
        }
        // Controllers/HomeController.cs

        public async Task<IActionResult> TopRated()
        {
            var topRatedServices = await _context.UserServices
                .Include(us => us.Service)
                    .ThenInclude(s => s.ServiceCategory)
                .Include(us => us.Artist)
                    .ThenInclude(a => a.ArtistProfile)
                .Where(us => us.IsActive)
                .Select(us => new
                {
                    Service = us,
                    AverageRating = _context.Reviews
                        .Where(r => r.Booking.UserServiceId == us.Id)
                        .Average(r => (double?)r.Rating) ?? 0,
                    ReviewCount = _context.Reviews
                        .Where(r => r.Booking.UserServiceId == us.Id)
                        .Count()
                })
                .Where(x => x.ReviewCount >= 3)
                .OrderByDescending(x => x.AverageRating)
                .ThenByDescending(x => x.ReviewCount)
                .Select(x => x.Service)
                .ToListAsync();

            var model = new ServiceListViewModel
            {
                Title = "? Top Rated Services",
                Services = topRatedServices.Select(us => new ServiceListViewModel.ServiceItem
                {
                    UserServiceId = us.Id,
                    ServiceName = us.Service?.Name ?? "Unnamed",
                    Description = us.CustomDescription ?? us.Service?.Description ?? "",
                    Category = us.Service?.ServiceCategory?.Name ?? "Uncategorized",
                    CategoryId = us.Service?.CategoryId ?? 0,
                    Price = us.Price,
                    ImagePath = us.ImagePath ?? us.Service?.ImagePath,
                    ArtistName = !string.IsNullOrEmpty(us.Artist?.FirstName)
                        ? $"{us.Artist.FirstName} {us.Artist.LastName}".Trim()
                        : us.Artist?.UserName ?? "Pro Artist",
                    ArtistId = us.ArtistId,
                    City = us.Artist?.ArtistProfile?.City ?? "Unknown",
                    Province = us.Artist?.ArtistProfile?.Province ?? "",
                    AverageRating = _context.Reviews
                        .Where(r => r.Booking.UserServiceId == us.Id)
                        .Average(r => (double?)r.Rating) ?? 0,
                    ReviewCount = _context.Reviews
                        .Where(r => r.Booking.UserServiceId == us.Id)
                        .Count()
                }).ToList()
            };

            return View("ServiceList", model);
        }
        public async Task<IActionResult> AllServices()
        {
            var services = await _context.UserServices
                .Include(us => us.Service)
                    .ThenInclude(s => s.ServiceCategory) // FIXED
                .Include(us => us.Artist)
                     .ThenInclude(a => a.ArtistProfile) 
                .Where(us => us.IsActive)
                .ToListAsync();

            var model = new ServiceListViewModel
            {
                Title = "All Services",
                Services = services.Select(us => new ServiceListViewModel.ServiceItem
                {
                    UserServiceId = us.Id,
                    ServiceName = us.Service?.Name ?? "Unnamed",
                    Description = us.CustomDescription ?? us.Service?.Description ?? "",
                    Category = us.Service?.ServiceCategory?.Name ?? "Uncategorized",
                    CategoryId = us.Service?.CategoryId ?? 0,       // ? ADDED
                    Price = us.Price,
                    ImagePath = us.ImagePath ?? us.Service?.ImagePath,
                    ArtistName = !string.IsNullOrEmpty(us.Artist?.FirstName)
                        ? $"{us.Artist.FirstName} {us.Artist.LastName}".Trim()
                        : us.Artist?.UserName ?? "Pro Artist",
                    ArtistId = us.ArtistId,
                    City = us.Artist?.ArtistProfile?.City ?? "Unknown"
                }).ToList()
            };

            return View("ServiceList", model);
        }
        [Route("Home/Artists")]
        [Route("Home/BrowseArtists")]
        public async Task<IActionResult> BrowseArtists()
        {
            var artists = await _context.Users
                .Where(u => u.ArtistProfile != null)
                .Include(u => u.ArtistProfile)
                .Include(u => u.UserServices.Where(us => us.IsActive))
                    .ThenInclude(us => us.Service)
                        .ThenInclude(s => s.ServiceCategory) //  FIXED (was .Category)
                .ToListAsync();

            var model = artists.Select(a => new BrowseArtistViewModel
            {
                ArtistId = a.Id,
                FullName = !string.IsNullOrEmpty(a.FirstName)
                    ? $"{a.FirstName} {a.LastName}".Trim()
                    : a.ArtistProfile?.FullName
                      ?? a.UserName
                      ?? a.Email,
                Province = a.ArtistProfile?.Province ?? "Unknown",
                City = a.ArtistProfile?.City ?? "",
                ProfilePictureUrl = a.ArtistProfile?.ProfilePictureUrl
                                    ?? "/images/default-profile.png",
                ContactInfo = a.ArtistProfile?.ContactInfo,       // ? ADD
                InstagramUrl = a.ArtistProfile?.InstagramUrl,     // ? ADD
                YearsExperience = a.ArtistProfile?.YearsExperience ?? 0, // ? ADD
                Bio = a.ArtistProfile?.Bio,
                Services = a.UserServices
                    .Where(us => us.IsActive)
                    .Take(3)
                    .Select(us => us.Service?.Name ?? "Unnamed Service")
                    .ToList()
            }).ToList();

            return View("BrowseArtists", model);
        }
        [AllowAnonymous]
        public async Task<IActionResult> Catalogue(string artistId, int categoryId)
        {
            if (string.IsNullOrEmpty(artistId)) return NotFound();

            // Get artist details
            var artist = await _context.Users
                .Include(u => u.ArtistProfile)
                .FirstOrDefaultAsync(u => u.Id == artistId);
            if (artist == null) return NotFound();

            // Get category details
            var category = await _context.ServiceCategories
                .FirstOrDefaultAsync(c => c.Id == categoryId);
            if (category == null) return NotFound();

            // Get ALL services by this artist in this category
            var userServices = await _context.UserServices
                .Where(us => us.ArtistId == artistId
                          && us.IsActive
                          && us.Service.CategoryId == categoryId)
                .Include(us => us.Service)
                    .ThenInclude(s => s.ServiceCategory)
                .Include(us => us.Artist)
                .ToListAsync();

            ViewBag.Portfolios = await _context.Portfolios
                .Include(p => p.Items)
                .Where(p => p.ArtistId == artistId)
                .ToListAsync();

            var artistName = !string.IsNullOrEmpty(artist.FirstName)
                ? $"{artist.FirstName} {artist.LastName}".Trim()
                : artist.UserName ?? "Pro Artist";

            var model = new ServiceListViewModel
            {
                Title = $"{artistName} — {category.Name}",
                ArtistId = artist.Id,
                ArtistName = artistName,
                ArtistLocation = !string.IsNullOrEmpty(artist.ArtistProfile?.City)
    ? $"{artist.ArtistProfile.City}, {artist.ArtistProfile.Province}"
    : artist.ArtistProfile?.Province ?? "",
                ArtistProfilePicture = artist.ArtistProfile?.ProfilePictureUrl
                                       ?? "/images/default-profile.png",
                Services = userServices.Select(us => new ServiceListViewModel.ServiceItem
                {
                    UserServiceId = us.Id,
                    ServiceName = us.Service?.Name ?? "No Name",
                    Description = us.CustomDescription ?? us.Service?.Description ?? "",
                    Category = us.Service?.ServiceCategory?.Name ?? "Uncategorized",
                    CategoryId = us.Service?.CategoryId ?? 0,
                    Price = us.Price,
                    ImagePath = us.ImagePath ?? us.Service?.ImagePath,
                    ArtistName = artistName,
                    ArtistId = us.ArtistId, // never forget
                }).ToList()
            };

            return View("Catalogue", model); // NOT ServiceList anymore
        }


        [Route("Home/CategoryServices/{categoryId}")] // This fixes the 404
        public async Task<IActionResult> CategoryServices(int categoryId)
        {
            // 1. Get the Category details
            var category = await _context.ServiceCategories
                .FirstOrDefaultAsync(c => c.Id == categoryId);

            if (category == null) return NotFound();

            // 2. Fetch ONLY services that belong to this category
            var userServices = await _context.UserServices
                .Include(us => us.Service)
                    .ThenInclude(s => s.ServiceCategory) // Ensure Category is included for the name
                .Include(us => us.Artist)
                     .ThenInclude(a => a.ArtistProfile)
                .Where(us => us.IsActive && us.Service.CategoryId == categoryId)
                .ToListAsync();

            var allForDebug = await _context.UserServices.Include(u => u.Service).Where(u => u.IsActive).ToListAsync();
            TempData["Debug"] = $"CLICKED={categoryId} FOUND={userServices.Count} ALL_ACTIVE={allForDebug.Count} CATS=" +
                string.Join(",", allForDebug.Select(u => $"{u.Service?.Name}:CatId={u.Service?.CategoryId}"));

            // Fetch portfolio items for this category to pass to the modal
            ViewBag.Portfolios = await _context.PortfolioItems
                .Where(pi => pi.CategoryId == categoryId)
                .ToListAsync();

            // 3. Map to your existing ViewModel
            var model = new ServiceListViewModel
            {
                Title = $"Services in {category.Name}",
                Category = category,
                Services = userServices.Select(us => new ServiceListViewModel.ServiceItem
                {
                    UserServiceId = us.Id,
                    ServiceName = us.Service?.Name ?? "Unnamed",
                    Description = us.CustomDescription ?? us.Service?.Description ?? "",
                    Category = us.Service?.ServiceCategory?.Name ?? "Uncategorized",
                    CategoryId = us.Service?.CategoryId ?? 0,
                    Price = us.Price,
                    ImagePath = us.ImagePath ?? us.Service?.ImagePath,
                    ArtistName = !string.IsNullOrEmpty(us.Artist?.FirstName)
    ? $"{us.Artist.FirstName} {us.Artist.LastName}".Trim()
    : us.Artist?.UserName ?? "Pro Artist",
                    ArtistId = us.ArtistId,
                    City = us.Artist?.ArtistProfile?.City ?? "Unknown"
                }).ToList()
            };

            return View("ServiceList", model);
        }
    }

}
