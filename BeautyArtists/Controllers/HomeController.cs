using BeautyArtists.Data;
using BeautyArtists.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using BeautyArtists.Models.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.Build.Framework;

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
            var featuredServices = await _context.Services
                 .Include(s => s.Category)
                 .Where(s => s.IsFeatured)
                 .ToListAsync();


            var testimonials = await _context.Testimonials.ToListAsync();

            return View(new HomeViewModel
            {
                HeroImagePath = "/uploads/hero/hero1.jpg",
                FeaturedServices = featuredServices,
                Testimonials = testimonials
            });


         


        }
        public async Task<IActionResult> BrowseArtists()
        {
            var artists = await _context.Users
                .Where(u => u.ArtistProfile != null)
                .Include(u => u.ArtistProfile)
                .Include(u => u.UserServices.Where(us => us.IsActive))
                     .ThenInclude(us => us.Service)
                     .ThenInclude(s => s.Category)
                .ToListAsync();

            var model = artists.Select(a => new BrowseArtistViewModel
            {
                ArtistId = a.Id,
                FullName = a.ArtistProfile?.FullName ?? $"{a.FirstName} {a.LastName}" ?? a.UserName ?? a.Email,
                Location = a.ArtistProfile?.Location ?? "Unknown",
                ProfilePictureUrl = a.ArtistProfile?.ProfilePictureUrl ?? "/images/default-profile.png",
                Services = a.UserServices
                     .Where(us => us.IsActive)
                     .Take(3)
                     .Select(us => us.Service?.Name ?? "Unnamed Service")
                     .ToList()
            }).ToList();

            return View(model);
        }


        public async Task<IActionResult> ViewServices(string artistId)
        {
            if (string.IsNullOrEmpty(artistId))
                return NotFound();

            var artist = await _context.Users
                .Include(u => u.ArtistProfile)
                .FirstOrDefaultAsync(u => u.Id == artistId);

            if (artist == null)
                return NotFound();

            var userServices = await _context.UserServices
                .Where(us => us.ArtistId == artistId && us.IsActive)
                .Include(us => us.Service)
                    .ThenInclude(s => s.Category)
                .ToListAsync();

            var model = new ArtistServiceViewModel
            {
                ArtistId = artist.Id,
                FullName = artist.ArtistProfile?.FullName
                           ?? $"{artist.FirstName} {artist.LastName}"
                           ?? artist.UserName
                           ?? artist.Email,
                Location = artist.ArtistProfile?.Location ?? "Unknown",
                ProfilePictureUrl = artist.ArtistProfile?.ProfilePictureUrl ?? "/images/default-profile.png",
                Services = userServices.Select(us => new ArtistServiceViewModel.ServiceItem
                {
                    UserServiceId = us.Id,
                    ServiceName = us.Service?.Name ?? "No Name",
                    Description = us.Service?.Description ?? "",
                    Category = us.Service?.Category?.Name ?? "Uncategorized",
                    Price = us.Price
                }).ToList()
            };

            return View(model);
        }


        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

    }
}
