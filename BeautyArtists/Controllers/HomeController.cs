using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Build.Framework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Net;
using System.Net.Mail;


namespace BeautyArtists.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _env;

        public HomeController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IConfiguration configuration, IWebHostEnvironment env)
        {
            _context = context;
            _userManager = userManager;
            _configuration = configuration;
            _env = env;
        }

        // ???????????????????????????????????????????????????????????
        //  INDEX - Homepage with banners, categories, featured & top rated
        // ???????????????????????????????????????????????????????????
        public async Task<IActionResult> Index()
        {
            // ?? FIX: Run queries sequentially (DbContext is NOT thread-safe)
            var banners = await _context.HeroBanners.AsNoTracking().ToListAsync();
            var categories = await _context.ServiceCategories.AsNoTracking().ToListAsync();
            var testimonials = await _context.Testimonials.AsNoTracking().ToListAsync();

            // One service per artist — newest active one
            var featuredServices = await _context.UserServices
                .Include(us => us.Service)
                .Include(us => us.Artist)
                .Where(us => us.IsActive)
                .AsNoTracking()
                .GroupBy(us => us.ArtistId)
                .Select(g => g.OrderByDescending(us => us.Id).First())
                .Take(6)
                .ToListAsync();

            // Top Rated Services (min 3 reviews, ordered by rating)
            var topRatedServices = await _context.UserServices
                .Include(us => us.Service)
                .Include(us => us.Artist)
                .Where(us => us.IsActive)
                .AsNoTracking()
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

        // ???????????????????????????????????????????????????????????
        //  SUPPORT
        // ???????????????????????????????????????????????????????????
        public IActionResult Support()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitReport(string category, string description, string email, List<IFormFile> attachments)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(category))
                {
                    TempData["Error"] = "Please select a category.";
                    return RedirectToAction(nameof(Support));
                }

                if (string.IsNullOrWhiteSpace(description))
                {
                    TempData["Error"] = "Please describe the issue.";
                    return RedirectToAction(nameof(Support));
                }

                if (string.IsNullOrWhiteSpace(email))
                {
                    TempData["Error"] = "Email address is required. Please enter your email so we can follow up.";
                    return RedirectToAction(nameof(Support));
                }

                if (!IsValidEmail(email))
                {
                    TempData["Error"] = "Please enter a valid email address.";
                    return RedirectToAction(nameof(Support));
                }

                var report = new SupportReport
                {
                    Category = category,
                    Description = description,
                    Email = email,
                    SubmittedAt = DateTime.UtcNow.AddHours(2)
                };
                _context.SupportReports.Add(report);
                await _context.SaveChangesAsync();

                var uploadedFilePaths = new List<string>();
                var uploadedFileUrls = new List<string>();

                if (attachments != null && attachments.Any())
                {
                    long totalSize = attachments.Sum(f => f.Length);
                    if (totalSize > 10_000_000)
                    {
                        TempData["Error"] = "Total file size exceeds 10MB. Please reduce the size and try again.";
                        return RedirectToAction(nameof(Support));
                    }

                    var uploadDir = Path.Combine(_env.WebRootPath, "uploads", "support");
                    if (!Directory.Exists(uploadDir))
                        Directory.CreateDirectory(uploadDir);

                    foreach (var file in attachments)
                    {
                        if (file.Length == 0) continue;
                        var fileName = $"{Guid.NewGuid():N}_{file.FileName}";
                        var filePath = Path.Combine(uploadDir, fileName);
                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await file.CopyToAsync(stream);
                        }
                        uploadedFilePaths.Add(filePath);
                        uploadedFileUrls.Add($"/uploads/support/{fileName}");
                    }
                }

                string subject = $"New Support Report: {category}";
                string body = $@"
            <h2>New Support Report</h2>
            <p><strong>Category:</strong> {category}</p>
            <p><strong>User Email:</strong> {email}</p>
            <p><strong>Description:</strong></p>
            <p>{description}</p>
            <p><strong>Submitted at:</strong> {DateTime.UtcNow.AddHours(2):yyyy-MM-dd HH:mm:ss}</p>
            {(uploadedFileUrls.Any() ? $"<p><strong>Attachments:</strong> {string.Join(", ", uploadedFileUrls)}</p>" : "")}
            <hr />
            <p style='color:#888;'>This report was submitted via the RubiOr support page.</p>
        ";

                string[] stakeholderEmails = new string[]
                {
                    "ignatiuslerefolo07101999@gmail.com",
                    "neo305mofokeng@gmail.com"
                };

                await SendEmailToMultipleAsync(stakeholderEmails, subject, body, uploadedFilePaths);

                TempData["Success"] = "Thank you! Your report has been submitted. We'll review it within 24 hours.";
                return RedirectToAction(nameof(Support));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SubmitReport Error: {ex.Message}\n{ex.StackTrace}");
                TempData["Error"] = "There was an issue submitting your report. Please try again or contact us directly.";
                return RedirectToAction(nameof(Support));
            }
        }

        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        private async Task SendEmailToMultipleAsync(string[] toEmails, string subject, string body, List<string> attachmentPaths = null)
        {
            var smtpClient = new SmtpClient
            {
                Host = _configuration["SmtpSettings:Host"],
                Port = int.Parse(_configuration["SmtpSettings:Port"]),
                Credentials = new NetworkCredential(
                    _configuration["SmtpSettings:Username"],
                    _configuration["SmtpSettings:Password"]
                ),
                EnableSsl = true,
                UseDefaultCredentials = false
            };

            using (var mailMessage = new MailMessage
            {
                From = new MailAddress(_configuration["SmtpSettings:FromAddress"], "RubiOr Support"),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            })
            {
                foreach (var email in toEmails)
                {
                    mailMessage.To.Add(email);
                }

                if (attachmentPaths != null && attachmentPaths.Any())
                {
                    foreach (var path in attachmentPaths)
                    {
                        if (System.IO.File.Exists(path))
                        {
                            var attachment = new Attachment(path);
                            mailMessage.Attachments.Add(attachment);
                        }
                    }
                }

                await smtpClient.SendMailAsync(mailMessage);
            }
        }

        //  CATEGORIES
        public async Task<IActionResult> Categories()
        {
            var categories = await _context.ServiceCategories
                .OrderBy(c => c.Name)
                .AsNoTracking()
                .ToListAsync();
            return View(categories);
        }

        
        //  VIEW SERVICE
        public async Task<IActionResult> ViewService(string artistId)
        {
            ViewBag.Portfolios = await _context.PortfolioItems.AsNoTracking().ToListAsync();

            if (string.IsNullOrEmpty(artistId)) return NotFound();

            var artist = await _context.Users
                .Include(u => u.ArtistProfile)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == artistId);

            if (artist == null) return NotFound();

            var userServices = await _context.UserServices
                .Where(us => us.ArtistId == artistId && us.IsActive)
                .Include(us => us.Service)
                    .ThenInclude(s => s.ServiceCategory)
                .Include(us => us.Artist)
                .AsNoTracking()
                .ToListAsync();

            ViewBag.Portfolios = await _context.Portfolios
                .Include(p => p.Items)
                .Where(p => p.ArtistId == artistId)
                .AsNoTracking()
                .ToListAsync();

            // GROUP by category — one representative service per category
            var groupedServices = userServices
                .GroupBy(us => us.Service?.ServiceCategory?.Name ?? "Other")
                .Select(g => g.First())
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
                ArtistProfilePicture = artist.ArtistProfile?.ProfilePictureUrl ?? "/images/default-profile.png",
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

        //  TOP RATED
        // 
        public async Task<IActionResult> TopRated()
        {
            try
            {
                Console.WriteLine("TopRated: Starting...");

                var topRatedServices = await _context.UserServices
                    .Include(us => us.Service)
                        .ThenInclude(s => s.ServiceCategory)
                    .Include(us => us.Artist)
                        .ThenInclude(a => a.ArtistProfile)
                    .Where(us => us.IsActive)
                    .AsNoTracking()
                    .Select(us => new
                    {
                        Service = us,
                        // ?? FIX: Handle null Booking references in Reviews
                        AverageRating = _context.Reviews
                            .Where(r => r.Booking != null && r.Booking.UserServiceId == us.Id)
                            .Average(r => (double?)r.Rating) ?? 0,
                        ReviewCount = _context.Reviews
                            .Where(r => r.Booking != null && r.Booking.UserServiceId == us.Id)
                            .Count()
                    })
                    .Where(x => x.ReviewCount >= 3)
                    .OrderByDescending(x => x.AverageRating)
                    .ThenByDescending(x => x.ReviewCount)
                    .Select(x => x.Service)
                    .ToListAsync();

                Console.WriteLine($"TopRated: Found {topRatedServices.Count} services with 3+ reviews");

                var serviceIds = topRatedServices.Select(s => s.Id).ToList();
                var allReviews = new List<Review>();

                if (serviceIds.Any())
                {
                    allReviews = await _context.Reviews
                        .Where(r => r.Booking != null && serviceIds.Contains(r.Booking.UserServiceId))
                        .AsNoTracking()
                        .ToListAsync();
                    Console.WriteLine($"TopRated: Found {allReviews.Count} reviews");
                }

                var model = new ServiceListViewModel
                {
                    Title = "Top Rated Services",
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
                        AverageRating = allReviews
                            .Where(r => r.Booking != null && r.Booking.UserServiceId == us.Id)
                            .Select(r => (double)r.Rating)
                            .DefaultIfEmpty(0)
                            .Average(),
                        ReviewCount = allReviews
                            .Count(r => r.Booking != null && r.Booking.UserServiceId == us.Id)
                    }).ToList(),
                    CurrentPage = 1,
                    TotalPages = 1,
                    TotalCount = topRatedServices.Count
                };

                Console.WriteLine($"? TopRated: Successfully built model with {model.Services.Count} services");
                return View("ServiceList", model);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? TopRated ERROR: {ex.Message}");
                Console.WriteLine($"Stack: {ex.StackTrace}");

                var emptyModel = new ServiceListViewModel
                {
                    Title = "? Top Rated Services",
                    Services = new List<ServiceListViewModel.ServiceItem>(),
                    CurrentPage = 1,
                    TotalPages = 1,
                    TotalCount = 0
                };
                return View("ServiceList", emptyModel);
            }
        }
        //  ALL SERVICES (with pagination)
        public async Task<IActionResult> AllServices(int page = 1, int pageSize = 12)
        {
            try
            {
                var query = _context.UserServices
                    .Include(us => us.Service)
                        .ThenInclude(s => s.ServiceCategory)
                    .Include(us => us.Artist)
                        .ThenInclude(a => a.ArtistProfile)
                    .Where(us => us.IsActive)
                    .AsNoTracking();

                var totalCount = await query.CountAsync();

                var services = await query
                    .OrderByDescending(us => us.Id)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var serviceIds = services.Select(s => s.ServiceId).ToList();
                var allReviews = new List<Review>();

                if (serviceIds.Any())
                {
                    allReviews = await _context.Reviews
                        .Where(r => r.Booking != null && serviceIds.Contains(r.ServiceId))
                        .AsNoTracking()
                        .ToListAsync();
                }

                var model = new ServiceListViewModel
                {
                    Title = "All Services",
                    Services = services.Select(us => new ServiceListViewModel.ServiceItem
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
                        City = us.Artist?.ArtistProfile?.City ?? "",
                        Province = us.Artist?.ArtistProfile?.Province ?? "",
                        ArtistLocation = !string.IsNullOrEmpty(us.Artist?.ArtistProfile?.City)
                            ? $"{us.Artist.ArtistProfile.City}, {us.Artist.ArtistProfile.Province}"
                            : us.Artist?.ArtistProfile?.Province ?? "",
                        ReviewCount = allReviews.Count(r => r.Booking != null && r.ServiceId == us.ServiceId),
                        AverageRating = allReviews
                            .Where(r => r.Booking != null && r.ServiceId == us.ServiceId)
                            .Select(r => (double)r.Rating)
                            .DefaultIfEmpty(0)
                            .Average()
                    }).ToList(),
                    CurrentPage = page,
                    TotalPages = (int)Math.Ceiling((double)totalCount / pageSize),
                    TotalCount = totalCount
                };

                return View("ServiceList", model);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? AllServices ERROR: {ex.Message}");
                var emptyModel = new ServiceListViewModel
                {
                    Title = "All Services",
                    Services = new List<ServiceListViewModel.ServiceItem>(),
                    CurrentPage = 1,
                    TotalPages = 1,
                    TotalCount = 0
                };
                return View("ServiceList", emptyModel);
            }
        }

        //  BROWSE ARTISTS
        [Route("Home/Artists")]
        [Route("Home/BrowseArtists")]
        public async Task<IActionResult> BrowseArtists(int page = 1, int pageSize = 12)
        {
            var query = _context.Users
                .Where(u => u.ArtistProfile != null)
                .Include(u => u.ArtistProfile)
                .Include(u => u.UserServices.Where(us => us.IsActive))
                    .ThenInclude(us => us.Service)
                        .ThenInclude(s => s.ServiceCategory)
                .AsNoTracking();

            var totalCount = await query.CountAsync();

            var artists = await query
                .OrderBy(u => u.FirstName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var model = artists.Select(a => new BrowseArtistViewModel
            {
                ArtistId = a.Id,
                FullName = !string.IsNullOrEmpty(a.FirstName)
                    ? $"{a.FirstName} {a.LastName}".Trim()
                    : a.ArtistProfile?.FullName ?? a.UserName ?? a.Email,
                Province = a.ArtistProfile?.Province ?? "Unknown",
                City = a.ArtistProfile?.City ?? "",
                ProfilePictureUrl = a.ArtistProfile?.ProfilePictureUrl ?? "/images/default-profile.png",
                ContactInfo = a.ArtistProfile?.ContactInfo,
                InstagramUrl = a.ArtistProfile?.InstagramUrl,
                YearsExperience = a.ArtistProfile?.YearsExperience ?? 0,
                Bio = a.ArtistProfile?.Bio,
                Services = a.UserServices
                    .Where(us => us.IsActive)
                    .Take(3)
                    .Select(us => us.Service?.Name ?? "Unnamed Service")
                    .ToList()
            }).ToList();

            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            ViewBag.TotalCount = totalCount;

            return View("BrowseArtists", model);
        }

        // ???????????????????????????????????????????????????????????
        //  CATALOGUE
        // ???????????????????????????????????????????????????????????
        [AllowAnonymous]
        public async Task<IActionResult> Catalogue(string artistId, int categoryId)
        {
            if (string.IsNullOrEmpty(artistId)) return NotFound();

            var artistTask = _context.Users
                .Include(u => u.ArtistProfile)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == artistId);

            var categoryTask = _context.ServiceCategories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == categoryId);

            var userServicesTask = _context.UserServices
                .Where(us => us.ArtistId == artistId
                          && us.IsActive
                          && us.Service.CategoryId == categoryId)
                .Include(us => us.Service)
                    .ThenInclude(s => s.ServiceCategory)
                .Include(us => us.Artist)
                .AsNoTracking()
                .ToListAsync();

            var portfoliosTask = _context.Portfolios
                .Include(p => p.Items)
                .Where(p => p.ArtistId == artistId)
                .AsNoTracking()
                .ToListAsync();

            await Task.WhenAll(artistTask, categoryTask, userServicesTask, portfoliosTask);

            var artist = await artistTask;
            var category = await categoryTask;
            var userServices = await userServicesTask;
            var portfolios = await portfoliosTask;

            if (artist == null || category == null) return NotFound();

            ViewBag.Portfolios = portfolios;

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
                ArtistProfilePicture = artist.ArtistProfile?.ProfilePictureUrl ?? "/images/default-profile.png",
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
                    ArtistId = us.ArtistId,
                }).ToList()
            };

            return View("Catalogue", model);
        }

        // ???????????????????????????????????????????????????????????
        //  CATEGORY SERVICES (with pagination)
        // ???????????????????????????????????????????????????????????
        [Route("Home/CategoryServices/{categoryId}")]
        public async Task<IActionResult> CategoryServices(int categoryId, int page = 1, int pageSize = 12)
        {
            var category = await _context.ServiceCategories
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == categoryId);

            if (category == null) return NotFound();

            var query = _context.UserServices
                .Include(us => us.Service)
                    .ThenInclude(s => s.ServiceCategory)
                .Include(us => us.Artist)
                    .ThenInclude(a => a.ArtistProfile)
                .Where(us => us.IsActive && us.Service.CategoryId == categoryId)
                .AsNoTracking();

            var totalCount = await query.CountAsync();

            var userServices = await query
                .OrderByDescending(us => us.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Get reviews for these services
            var serviceIds = userServices.Select(s => s.ServiceId).ToList();
            var allReviews = await _context.Reviews
                .Where(r => serviceIds.Contains(r.ServiceId))
                .AsNoTracking()
                .ToListAsync();

            // Portfolio items
            ViewBag.Portfolios = await _context.PortfolioItems
                .Where(pi => pi.CategoryId == categoryId)
                .AsNoTracking()
                .ToListAsync();

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
                    City = us.Artist?.ArtistProfile?.City ?? "",
                    ArtistLocation = !string.IsNullOrEmpty(us.Artist?.ArtistProfile?.City)
                        ? $"{us.Artist.ArtistProfile.City}, {us.Artist.ArtistProfile.Province}"
                        : us.Artist?.ArtistProfile?.Province ?? "",
                    ReviewCount = allReviews.Count(r => r.ServiceId == us.ServiceId),
                    AverageRating = allReviews.Where(r => r.ServiceId == us.ServiceId).Any()
                        ? Math.Round(allReviews.Where(r => r.ServiceId == us.ServiceId).Average(r => r.Rating), 1)
                        : 0
                }).ToList(),
                CurrentPage = page,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize),
                TotalCount = totalCount
            };

            return View("ServiceList", model);
        }
    }
}