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

        // ─── PRICING CONSTANTS ───
        private const decimal CLIENT_MARKUP = 0.04m;      // 4% card processing fee
        private const decimal BOOKING_FEE = 5.00m;        // R5 booking fee
        private const decimal COMMISSION_NEW = 0.10m;      // 10% for new clients
        private const decimal COMMISSION_REPEAT = 15.00m;  // R15 for repeat clients
        private const decimal MIN_COMMISSION = 8.00m;      // Minimum R8 safeguard

        public EarningsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // ─── ✅ FIXED: Check by SPECIFIC SERVICE (UserServiceId) ───
        private async Task<bool> IsNewClient(int userServiceId, string customerId)
        {
            var existingBookings = await _context.Bookings
                .Where(b => b.UserServiceId == userServiceId
                       && b.CustomerId == customerId
                       && b.Status != Booking.BookingStatus.Cancelled
                       && b.Status != Booking.BookingStatus.Rejected)
                .AnyAsync();

            return !existingBookings;
        }

        // ─── HELPER: Calculate platform fee (10% or R15) ───
        private decimal GetPlatformFee(decimal servicePrice, bool isNewClient)
        {
            var platformFee = isNewClient ? servicePrice * COMMISSION_NEW : COMMISSION_REPEAT;
            if (platformFee < MIN_COMMISSION)
                platformFee = MIN_COMMISSION;
            return platformFee;
        }

        // ─── HELPER: Calculate what artist actually gets ───
        private decimal GetArtistPayout(decimal servicePrice, bool isNewClient)
        {
            var platformFee = GetPlatformFee(servicePrice, isNewClient);
            return servicePrice - platformFee;
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
            decimal totalLifetimeEarnings = 0m;
            decimal thisMonthEarnings = 0m;

            foreach (var b in completedBookings)
            {
                // ✅ FIXED: Use UserServiceId
                var isNew = await IsNewClient(b.UserServiceId, b.CustomerId);
                var artistEarnings = GetArtistPayout(b.ServicePrice, isNew);
                totalLifetimeEarnings += artistEarnings;

                if (b.AppointmentDate.Month == now.Month && b.AppointmentDate.Year == now.Year)
                    thisMonthEarnings += artistEarnings;
            }

            var pendingEarnings = allBookings
                .Where(b => b.Status != Booking.BookingStatus.Completed
                         && b.Status != Booking.BookingStatus.Cancelled
                         && b.Status != Booking.BookingStatus.Rejected)
                .Sum(b => b.ServicePrice);

            var completedCount = completedBookings.Count;
            var avgJobValue = completedCount > 0 ? totalLifetimeEarnings / completedCount : 0;

            // ─── TOP SERVICES ───
            var topServices = new List<KeyValuePair<string, EarningsServiceSummary>>();
            foreach (var group in completedBookings.GroupBy(b => b.UserService.Service.Name))
            {
                decimal totalEarnings = 0m;
                foreach (var b in group)
                {
                    // ✅ FIXED: Use UserServiceId
                    var isNew = await IsNewClient(b.UserServiceId, b.CustomerId);
                    totalEarnings += GetArtistPayout(b.ServicePrice, isNew);
                }
                topServices.Add(new KeyValuePair<string, EarningsServiceSummary>(
                    group.Key,
                    new EarningsServiceSummary
                    {
                        TotalEarnings = totalEarnings,
                        JobCount = group.Count()
                    }));
            }
            topServices = topServices.OrderByDescending(x => x.Value.TotalEarnings).ToList();

            // ─── PAGINATE ───
            var totalCount = allBookings.Count;
            var paginatedBookings = allBookings
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // ─── ✅ FIXED HISTORY WITH ALL VALUES POPULATED ───
            var history = new List<EarningsHistoryItem>();
            foreach (var b in paginatedBookings)
            {
                // ✅ FIXED: Use UserServiceId
                var isNew = await IsNewClient(b.UserServiceId, b.CustomerId);
                var platformFee = GetPlatformFee(b.ServicePrice, isNew);
                var artistPayout = GetArtistPayout(b.ServicePrice, isNew);

                // ─── CLIENT TOTAL (ServicePrice + Card Fee + Booking Fee) ───
                var cardFee = b.ServicePrice * CLIENT_MARKUP;  // 4%
                var bookingFee = BOOKING_FEE;                  // R5
                var clientTotalPaid = b.ServicePrice + cardFee + bookingFee;

                // ─── PLATFORM EARNINGS (Commission + Booking Fee) ───
                var platformTotalEarnings = platformFee + bookingFee;

                // ─── DEPOSIT & FINAL ───
                decimal depositPaid = b.IsDepositPaid ? b.DepositAmount : 0m;
                decimal finalPaid = b.FinalPaymentPaid > 0 ? b.FinalAmount : 0m;
                bool isFullyPaid = (b.DepositPaid + b.FinalPaymentPaid) >= b.TotalAmount;

                history.Add(new EarningsHistoryItem
                {
                    BookingId = b.Id,
                    Date = b.AppointmentDate,
                    ClientName = $"{b.Customer?.FirstName} {b.Customer?.LastName}" ?? "Unknown",
                    ServiceName = b.UserService?.Service?.Name ?? "Service",

                    OriginalPrice = b.ServicePrice,
                    IsNewClient = isNew,

                    // ─── ARTIST SEES ───
                    YourEarnings = isFullyPaid ? artistPayout : 0m,
                    DepositPaid = depositPaid,
                    FinalPaymentPaid = finalPaid,
                    IsFullyPaid = isFullyPaid,
                    Status = b.Status.ToString(),

                    // ─── ✅ NOW POPULATED CORRECTLY ───
                    PlatformFee = platformFee,                    // Commission
                    BookingFee = bookingFee,                      // R5 booking fee
                    ClientTotalPaid = clientTotalPaid,            // What client paid total
                    PlatformTotalEarnings = platformTotalEarnings, // Platform revenue
                    TotalPaid = b.DepositPaid + b.FinalPaymentPaid,
                    TipAmount = 0m
                });
            }

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
                TotalArtistGross = totalLifetimeEarnings,

                TotalPlatformLifetimeEarnings = history.Sum(h => h.PlatformTotalEarnings),
                TotalBookingFeesCollected = history.Sum(h => h.BookingFee),
                TotalCommissionCollected = history.Sum(h => h.PlatformFee),
                ThisMonthPlatformEarnings = history
                    .Where(h => h.Date.Month == now.Month && h.Date.Year == now.Year)
                    .Sum(h => h.PlatformTotalEarnings),
                TotalBookingFees = history.Sum(h => h.BookingFee),
                TotalClientPaid = history.Sum(h => h.ClientTotalPaid)
            };

            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            ViewBag.TotalCount = totalCount;
            ViewBag.PageSize = pageSize;
            ViewBag.FullyPaidCount = history.Count(h => h.IsFullyPaid);

            return View(model);
        }

        // ─── CSV DOWNLOAD ───
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

            // ─── CSV HEADERS ───
            csv.AppendLine("Date,Client,Service,Service Price,Platform Fee,Your Earnings,Deposit Received,Final Payment Received,Fully Paid,Status,Client Type");

            foreach (var b in bookings)
            {
                var clientName = $"{b.Customer?.FirstName} {b.Customer?.LastName}".Replace("\"", "\"\"");
                var serviceName = (b.UserService?.Service?.Name ?? "Service").Replace("\"", "\"\"");

                // ✅ FIXED: Use UserServiceId
                var isNew = await IsNewClient(b.UserServiceId, b.CustomerId);
                var platformFee = GetPlatformFee(b.ServicePrice, isNew);
                var artistEarnings = GetArtistPayout(b.ServicePrice, isNew);

                decimal depositShare = b.IsDepositPaid ? b.DepositAmount : 0m;
                decimal finalShare = b.FinalPaymentPaid > 0 ? b.FinalAmount : 0m;
                bool isFullyPaid = (b.DepositPaid + b.FinalPaymentPaid) >= b.TotalAmount;
                var clientType = isNew ? "New" : "Repeat";

                csv.AppendLine($"\"{b.AppointmentDate:dd/MM/yyyy}\"," +
                              $"\"{clientName}\"," +
                              $"\"{serviceName}\"," +
                              $"\"R{b.ServicePrice:N2}\"," +
                              $"\"R{platformFee:N2}\"," +
                              $"\"R{(isFullyPaid ? artistEarnings : 0m):N2}\"," +
                              $"\"R{depositShare:N2}\"," +
                              $"\"R{finalShare:N2}\"," +
                              $"\"{(isFullyPaid ? "Yes" : "No")}\"," +
                              $"\"{b.Status}\"," +
                              $"\"{clientType}\"");
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