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

        // ============================================================
        // 🔥 HELPER METHODS - ARTIST ONLY SEES THEIR SERVICE PRICE
        // ============================================================

        // Get total client paid (deposit + final payment)
        private decimal GetTotalClientPaid(Booking b) => b.DepositPaid + b.FinalPaymentPaid;

        // 🔥 FIXED: Artist gets 85% of the SERVICE PRICE ONLY
        // Booking fee is HIDDEN from artist
        private decimal GetArtistCut(Booking b)
        {
            // If booking is completed, artist gets 85% of the service price
            if (b.Status == Booking.BookingStatus.Completed)
            {
                return b.ServicePrice * (1 - COMMISSION_RATE);
            }

            // For pending/accepted/confirmed: artist gets 85% of what's been paid toward SERVICE
            // DepositPaid includes booking fee, so subtract it
            decimal servicePaid = (b.DepositPaid - b.BookingFee) + b.FinalPaymentPaid;

            // If no service has been paid yet, return 0
            if (servicePaid <= 0) return 0m;

            return servicePaid * (1 - COMMISSION_RATE);
        }

        // 🔥 FIXED: Platform commission (15% of SERVICE PRICE only)
        private decimal GetPlatformCommission(Booking b)
        {
            if (b.Status == Booking.BookingStatus.Completed)
            {
                return b.ServicePrice * COMMISSION_RATE;
            }

            // For pending: commission on service portion only
            decimal servicePaid = (b.DepositPaid - b.BookingFee) + b.FinalPaymentPaid;
            if (servicePaid <= 0) return 0m;

            return servicePaid * COMMISSION_RATE;
        }

        // ============================================================
        // MAIN EARNINGS VIEW - ARTIST ONLY
        // ============================================================
        public async Task<IActionResult> Earnings(DateTime? fromDate, DateTime? toDate, string? statusFilter = null, string? serviceFilter = null, string? clientFilter = null)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var bookingsQuery = _context.Bookings
                .Include(b => b.Customer)
                .Include(b => b.UserService).ThenInclude(us => us.Service)
                .Where(b => b.UserService.ArtistId == user.Id);

            // --- FILTERS ---
            if (fromDate.HasValue)
                bookingsQuery = bookingsQuery.Where(b => b.AppointmentDate >= fromDate.Value);

            if (toDate.HasValue)
                bookingsQuery = bookingsQuery.Where(b => b.AppointmentDate < toDate.Value.AddDays(1));

            if (!string.IsNullOrEmpty(statusFilter))
            {
                if (Enum.TryParse<Booking.BookingStatus>(statusFilter, out var statusEnum))
                {
                    bookingsQuery = bookingsQuery.Where(b => b.Status == statusEnum);
                }
            }

            if (!string.IsNullOrEmpty(serviceFilter))
                bookingsQuery = bookingsQuery.Where(b => b.UserService.Service.Name.Contains(serviceFilter));

            if (!string.IsNullOrEmpty(clientFilter))
                bookingsQuery = bookingsQuery.Where(b =>
                    EF.Functions.Like((b.Customer.FirstName + " " + b.Customer.LastName), $"%{clientFilter}%"));

            // Execute Query
            var bookings = await bookingsQuery.OrderByDescending(b => b.AppointmentDate).ToListAsync();

            // --- VIEW DATA & CALCULATIONS ---
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
            ViewBag.SelectedStatus = statusFilter;
            ViewBag.SelectedService = serviceFilter;
            ViewBag.SelectedClient = clientFilter;

            var now = DateTime.Now;
            var completedBookings = bookings.Where(b => b.Status == Booking.BookingStatus.Completed).ToList();

            // ============================================================
            // 🔥 ARTIST EARNINGS CALCULATIONS - NO BOOKING FEE SHOWN
            // ============================================================

            // ─── ARTIST EARNINGS (85% of service price) ───
            var totalLifetimeEarnings = completedBookings
                .Sum(b => GetArtistCut(b));

            var thisMonthEarnings = completedBookings
                .Where(b => b.AppointmentDate.Month == now.Month && b.AppointmentDate.Year == now.Year)
                .Sum(b => GetArtistCut(b));

            var pendingEarnings = bookings
                .Where(b => b.Status != Booking.BookingStatus.Completed
                         && b.Status != Booking.BookingStatus.Cancelled
                         && b.Status != Booking.BookingStatus.Rejected)
                .Sum(b => {
                    decimal servicePaid = (b.DepositPaid - b.BookingFee) + b.FinalPaymentPaid;
                    return servicePaid * (1 - COMMISSION_RATE);
                });

            // ─── OTHER METRICS ───
            var completedCount = completedBookings.Count;
            var avgJobValue = completedCount > 0 ? totalLifetimeEarnings / completedCount : 0;

            var uniqueClients = completedBookings.Select(b => b.CustomerId).Distinct().Count();
            var repeatClientRate = uniqueClients > 0 ? Math.Max(0, (completedCount - uniqueClients) * 1.0 / completedCount) : 0;
            var utilizationRate = CalculateUtilization(bookings);

            var totalDeposits = bookings.Sum(b => b.DepositPaid);
            var totalFinalPayments = bookings.Sum(b => b.FinalPaymentPaid);

            // 🔥 ARTIST GROSS - SHOW THIS TO ARTIST (total service price before commission)
            var totalArtistGross = bookings
                .Where(b => b.Status != Booking.BookingStatus.Cancelled && b.Status != Booking.BookingStatus.Rejected)
                .Sum(b => b.ServicePrice);

            // ─── TOP SERVICES (artist earnings only) ───
            var topServices = completedBookings
                .GroupBy(b => b.UserService.Service.Name)
                .Select(g => new KeyValuePair<string, EarningsServiceSummary>(
                    g.Key,
                    new EarningsServiceSummary
                    {
                        TotalEarnings = g.Sum(b => GetArtistCut(b)),
                        JobCount = g.Count()
                    }))
                .OrderByDescending(x => x.Value.TotalEarnings)
                .ToList();

            // ─── HISTORY - ARTIST ONLY SEES THEIR SERVICE PRICE ───
            var history = bookings.Select(b => new EarningsHistoryItem
            {
                BookingId = b.Id,
                Date = b.AppointmentDate,
                ClientName = $"{b.Customer?.FirstName} {b.Customer?.LastName}" ?? "Unknown",
                ServiceName = b.UserService?.Service?.Name ?? "Service",

                // 🔥 ARTIST SEES THESE ONLY
                OriginalPrice = b.ServicePrice,          // Their service price
                YourEarnings = GetArtistCut(b),          // 85% of service price
                PlatformFee = GetPlatformCommission(b),  // 15% commission
                TipAmount = 0m,
                Status = b.Status.ToString(),

                // Payment breakdown (artist sees their deposits)
                DepositPaid = b.DepositPaid,
                FinalPaymentPaid = b.FinalPaymentPaid,
                IsFullyPaid = b.IsFullyPaid,
                TotalPaid = b.DepositPaid + b.FinalPaymentPaid,

                // 🔥 HIDDEN FROM ARTIST - NOT DISPLAYED
                BookingFee = 0m,                         // Hidden
                ClientTotalPaid = 0m,                    // Hidden
                PlatformTotalEarnings = 0m               // Hidden
            }).ToList();

            // ============================================================
            // 🔥 BUILD MODEL - ARTIST ONLY
            // ============================================================
            var model = new ArtistEarningsViewModel
            {
                // ─── ARTIST EARNINGS ONLY ───
                TotalLifetimeEarnings = totalLifetimeEarnings,
                ThisMonthEarnings = thisMonthEarnings,
                PendingEarnings = pendingEarnings,
                CompletedBookingsCount = completedCount,
                AvgJobValue = avgJobValue,
                RepeatClientRate = repeatClientRate,
                UtilizationRate = utilizationRate,
                TopServices = topServices,
                TotalDeposits = totalDeposits,
                TotalFinalPayments = totalFinalPayments,
                History = history,

                // ─── ARTIST GROSS (SHOW THIS) ───
                TotalArtistGross = totalArtistGross,

                // ─── PLATFORM EARNINGS (HIDDEN FROM ARTIST) ───
                TotalPlatformLifetimeEarnings = 0m,
                TotalBookingFeesCollected = 0m,
                TotalCommissionCollected = 0m,
                ThisMonthPlatformEarnings = 0m,

                // ─── BOOKING FEES & CLIENT TOTAL (HIDDEN FROM ARTIST) ───
                TotalBookingFees = 0m,
                TotalClientPaid = 0m
            };

            return View(model);
        }

        // ============================================================
        // CSV DOWNLOAD - ARTIST ONLY (NO BOOKING FEE)
        // ============================================================
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

            // 🔥 CSV HEADERS - ARTIST ONLY (NO BOOKING FEE)
            csv.AppendLine("Date,Client,Service,Service Price,Deposit,Final Payment,Your Cut (85%),Platform Fee (15%),Tip,Status");

            foreach (var b in bookings)
            {
                var clientName = $"{b.Customer?.FirstName} {b.Customer?.LastName}".Replace("\"", "\"\"");
                var serviceName = (b.UserService?.Service?.Name ?? "Service").Replace("\"", "\"\"");

                decimal artistCut = GetArtistCut(b);
                decimal platformCommission = GetPlatformCommission(b);

                csv.AppendLine($"\"{b.AppointmentDate:dd/MM/yyyy}\"," +
                              $"\"{clientName}\"," +
                              $"\"{serviceName}\"," +
                              $"\"R{b.ServicePrice:N2}\"," +
                              $"\"R{b.DepositPaid:N2}\"," +
                              $"\"R{b.FinalPaymentPaid:N2}\"," +
                              $"\"R{artistCut:N2}\"," +
                              $"\"R{platformCommission:N2}\"," +
                              $"\"R0.00\"," +
                              $"\"{b.Status}\"");
            }

            var csvBytes = Encoding.UTF8.GetBytes("\uFEFF" + csv.ToString());
            return File(csvBytes, "text/csv", $"ArtistEarnings_{DateTime.Now:yyyyMMdd_HHmm}.csv");
        }

        // ============================================================
        // UTILITY METHODS
        // ============================================================
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