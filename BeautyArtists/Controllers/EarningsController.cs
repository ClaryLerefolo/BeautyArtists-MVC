using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Models.ViewModels;
using System.Text;

namespace BeautyArtists.Controllers
{
    [Authorize(Roles = "Artist")]
    public class EarningsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private const decimal COMMISSION_RATE = 0.15m;
        private const decimal BOOKING_FEE = 5.00m;

        public EarningsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // ─── HELPER: Artist's earned amount (based on their price) ───
        private decimal GetArtistEarnings(Booking b)
        {
            // Artist earns 100% of their service price when fully paid
            // Platform adds 15% on top for the client, artist gets full amount
            if (b.Status == Booking.BookingStatus.Completed)
                return b.ServicePrice;

            // Otherwise, accumulate based on what they've been paid
            decimal earned = 0m;

            // Deposit covers 50% of their price
            if (b.IsDepositPaid)
                earned += b.ServicePrice / 2;

            // Final covers the other 50%
            if (b.FinalPaymentPaid > 0)
                earned += b.ServicePrice / 2;

            return Math.Min(earned, b.ServicePrice);
        }

        public async Task<IActionResult> Earnings(
            DateTime? fromDate,
            DateTime? toDate,
            string? statusFilter = null,
            string? serviceFilter = null,
            string? clientFilter = null,
            int page = 1,
            int pageSize = 10)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var bookingsQuery = _context.Bookings
                .Include(b => b.Customer)
                .Include(b => b.UserService).ThenInclude(us => us.Service)
                .Where(b => b.UserService.ArtistId == user.Id);

            // ─── FILTERS ───
            if (fromDate.HasValue)
                bookingsQuery = bookingsQuery.Where(b => b.AppointmentDate >= fromDate.Value);

            if (toDate.HasValue)
                bookingsQuery = bookingsQuery.Where(b => b.AppointmentDate < toDate.Value.AddDays(1));

            if (!string.IsNullOrEmpty(statusFilter))
            {
                if (Enum.TryParse<Booking.BookingStatus>(statusFilter, out var statusEnum))
                    bookingsQuery = bookingsQuery.Where(b => b.Status == statusEnum);
            }

            if (!string.IsNullOrEmpty(serviceFilter))
                bookingsQuery = bookingsQuery.Where(b => b.UserService.Service.Name.Contains(serviceFilter));

            if (!string.IsNullOrEmpty(clientFilter))
                bookingsQuery = bookingsQuery.Where(b =>
                    EF.Functions.Like((b.Customer.FirstName + " " + b.Customer.LastName), $"%{clientFilter}%"));

            var allBookings = await bookingsQuery.OrderByDescending(b => b.AppointmentDate).ToListAsync();

            // ─── VIEWBAG ───
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
            ViewBag.SelectedStatus = statusFilter;
            ViewBag.SelectedService = serviceFilter;
            ViewBag.SelectedClient = clientFilter;

            var now = DateTime.Now;
            var completedBookings = allBookings.Where(b => b.Status == Booking.BookingStatus.Completed).ToList();

            // ─── STATS (artist's earnings only) ───
            var totalLifetimeEarnings = completedBookings.Sum(b => GetArtistEarnings(b));
            var thisMonthEarnings = completedBookings
                .Where(b => b.AppointmentDate.Month == now.Month && b.AppointmentDate.Year == now.Year)
                .Sum(b => GetArtistEarnings(b));

            var pendingEarnings = allBookings
                .Where(b => b.Status != Booking.BookingStatus.Completed
                         && b.Status != Booking.BookingStatus.Cancelled
                         && b.Status != Booking.BookingStatus.Rejected)
                .Sum(b => GetArtistEarnings(b));

            var completedCount = completedBookings.Count;
            var avgJobValue = completedCount > 0 ? totalLifetimeEarnings / completedCount : 0;

            // ─── TOP SERVICES ───
            var topServices = completedBookings
                .GroupBy(b => b.UserService.Service.Name)
                .Select(g => new KeyValuePair<string, EarningsServiceSummary>(
                    g.Key,
                    new EarningsServiceSummary
                    {
                        TotalEarnings = g.Sum(b => GetArtistEarnings(b)),
                        JobCount = g.Count()
                    }))
                .OrderByDescending(x => x.Value.TotalEarnings)
                .ToList();

            // ─── PAGINATE ───
            var totalCount = allBookings.Count;
            var paginatedBookings = allBookings
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // ─── ✅ FIXED HISTORY (ONLY what artist sees) ───
            var history = paginatedBookings.Select(b => new EarningsHistoryItem
            {
                BookingId = b.Id,
                Date = b.AppointmentDate,
                ClientName = $"{b.Customer?.FirstName} {b.Customer?.LastName}" ?? "Unknown",
                ServiceName = b.UserService?.Service?.Name ?? "Service",

                // ✅ FIXED: Artist sees their service price
                OriginalPrice = b.ServicePrice,  // ← This was 0m! Now shows artist's price

                // ─── ARTIST ONLY SEES THESE ───
                YourEarnings = GetArtistEarnings(b),
                DepositPaid = b.IsDepositPaid ? b.ServicePrice / 2 : 0m,
                FinalPaymentPaid = b.FinalPaymentPaid > 0 ? b.ServicePrice / 2 : 0m,
                IsFullyPaid = (b.DepositPaid - b.BookingFee) + b.FinalPaymentPaid >= b.ServicePrice,
                Status = b.Status.ToString(),

                // ─── HIDDEN FROM ARTIST (set to 0 or hidden) ───
                PlatformFee = 0m,
                TipAmount = 0m,
                BookingFee = 0m,
                ClientTotalPaid = 0m,
                PlatformTotalEarnings = 0m,
                TotalPaid = 0m
            }).ToList();

            // ─── MODEL ───
            var model = new ArtistEarningsViewModel
            {
                TotalLifetimeEarnings = totalLifetimeEarnings,
                ThisMonthEarnings = thisMonthEarnings,
                PendingEarnings = pendingEarnings,
                CompletedBookingsCount = completedCount,
                AvgJobValue = avgJobValue,
                RepeatClientRate = 0,
                UtilizationRate = 0,
                TopServices = topServices,
                TotalDeposits = history.Sum(h => h.DepositPaid),
                TotalFinalPayments = history.Sum(h => h.FinalPaymentPaid),
                History = history,

                // ─── ARTIST ONLY SEES THESE ───
                TotalArtistGross = totalLifetimeEarnings,

                // ─── HIDDEN FROM ARTIST ───
                TotalPlatformLifetimeEarnings = 0m,
                TotalBookingFeesCollected = 0m,
                TotalCommissionCollected = 0m,
                ThisMonthPlatformEarnings = 0m,
                TotalBookingFees = 0m,
                TotalClientPaid = 0m
            };

            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            ViewBag.TotalCount = totalCount;
            ViewBag.PageSize = pageSize;
            ViewBag.FullyPaidCount = history.Count(h => h.IsFullyPaid);

            return View(model);
        }

        // ─── CSV DOWNLOAD (ONLY what artist sees) ───
        [HttpGet("Earnings/DownloadCsv")]
        public async Task<IActionResult> DownloadCsv(DateTime? fromDate, DateTime? toDate, string? statusFilter = null, string? serviceFilter = null, string? clientFilter = null)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var bookingsQuery = _context.Bookings
                .Include(b => b.Customer)
                .Include(b => b.UserService).ThenInclude(us => us.Service)
                .Where(b => b.UserService.ArtistId == user.Id);

            if (fromDate.HasValue) bookingsQuery = bookingsQuery.Where(b => b.AppointmentDate >= fromDate.Value);
            if (toDate.HasValue) bookingsQuery = bookingsQuery.Where(b => b.AppointmentDate < toDate.Value.AddDays(1));
            if (!string.IsNullOrEmpty(statusFilter)) bookingsQuery = bookingsQuery.Where(b => b.Status.ToString() == statusFilter);
            if (!string.IsNullOrEmpty(serviceFilter)) bookingsQuery = bookingsQuery.Where(b => b.UserService.Service.Name.Contains(serviceFilter));
            if (!string.IsNullOrEmpty(clientFilter)) bookingsQuery = bookingsQuery.Where(b => EF.Functions.Like((b.Customer.FirstName + " " + b.Customer.LastName), $"%{clientFilter}%"));

            var bookings = await bookingsQuery.OrderByDescending(b => b.AppointmentDate).ToListAsync();

            var csv = new StringBuilder();

            // ─── CSV HEADERS (ONLY what artist sees) ───
            csv.AppendLine("Date,Client,Service,Service Price,Deposit Received,Final Payment Received,Total Earnings,Status");

            foreach (var b in bookings)
            {
                var clientName = $"{b.Customer?.FirstName} {b.Customer?.LastName}".Replace("\"", "\"\"");
                var serviceName = (b.UserService?.Service?.Name ?? "Service").Replace("\"", "\"\"");

                decimal artistEarnings = GetArtistEarnings(b);
                decimal depositShare = b.IsDepositPaid ? b.ServicePrice / 2 : 0m;
                decimal finalShare = b.FinalPaymentPaid > 0 ? b.ServicePrice / 2 : 0m;

                csv.AppendLine($"\"{b.AppointmentDate:dd/MM/yyyy}\"," +
                              $"\"{clientName}\"," +
                              $"\"{serviceName}\"," +
                              $"\"R{b.ServicePrice:N2}\"," +
                              $"\"R{depositShare:N2}\"," +
                              $"\"R{finalShare:N2}\"," +
                              $"\"R{artistEarnings:N2}\"," +
                              $"\"{b.Status}\"");
            }

            var csvBytes = Encoding.UTF8.GetBytes("\uFEFF" + csv.ToString());
            return File(csvBytes, "text/csv", $"ArtistEarnings_{DateTime.Now:yyyyMMdd_HHmm}.csv");
        }

        private double CalculateUtilization(List<Booking> bookings)
        {
            if (!bookings.Any()) return 0;
            var firstBookingDate = bookings.MinBy(b => b.AppointmentDate).AppointmentDate;
            var monthsSinceFirst = (DateTime.Now.Year * 12 + DateTime.Now.Month) -
                                  (firstBookingDate.Year * 12 + firstBookingDate.Month);
            var monthsWithBookings = bookings
                .Select(b => new { b.AppointmentDate.Year, b.AppointmentDate.Month })
                .Distinct()
                .Count();
            return monthsSinceFirst > 0 ? Math.Min(1.0, monthsWithBookings * 1.0 / monthsSinceFirst) : 0;
        }
    }
}