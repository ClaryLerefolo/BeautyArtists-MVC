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
        private const decimal _commissionRate = 0.15m;

        public EarningsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

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
            // 🔥 NEW: Use actual paid amounts (DepositPaid + FinalPaymentPaid)
            // ============================================================
            // Helper to get total paid for a booking (safe)
            decimal GetTotalPaid(Booking b) => b.DepositPaid + b.FinalPaymentPaid;

            // Lifetime earnings from completed bookings (artist cut)
            var totalLifetimeEarnings = completedBookings
                .Sum(b => GetTotalPaid(b) * (1 - _commissionRate));

            // Earnings this month from completed bookings
            var thisMonthEarnings = completedBookings
                .Where(b => b.AppointmentDate.Month == now.Month && b.AppointmentDate.Year == now.Year)
                .Sum(b => GetTotalPaid(b) * (1 - _commissionRate));

            // Pending earnings: money already paid (deposit + any partial final) on non‑completed, non‑cancelled bookings
            var pendingEarnings = bookings
                .Where(b => b.Status != Booking.BookingStatus.Completed && b.Status != Booking.BookingStatus.Cancelled)
                .Sum(b => GetTotalPaid(b) * (1 - _commissionRate));

            var completedCount = completedBookings.Count;
            var avgJobValue = completedCount > 0 ? totalLifetimeEarnings / completedCount : 0;

            var uniqueClients = completedBookings.Select(b => b.CustomerId).Distinct().Count();
            var repeatClientRate = uniqueClients > 0 ? Math.Max(0, (completedCount - uniqueClients) * 1.0 / completedCount) : 0;
            var utilizationRate = CalculateUtilization(bookings);
           
            var totalDeposits = bookings.Sum(b => b.DepositPaid);
            var totalFinalPayments = bookings.Sum(b => b.FinalPaymentPaid);

            // Top services based on actual paid amounts (completed only)
            var topServices = completedBookings
                .GroupBy(b => b.UserService.Service.Name)
                .Select(g => new KeyValuePair<string, EarningsServiceSummary>(
                    g.Key,
                    new EarningsServiceSummary
                    {
                        TotalEarnings = g.Sum(b => GetTotalPaid(b) * (1 - _commissionRate)),
                        JobCount = g.Count()
                    }))
                .OrderByDescending(x => x.Value.TotalEarnings)
                .ToList();

            // Build history with correct earnings
            var history = bookings.Select(b => new EarningsHistoryItem
            {
                BookingId = b.Id,
                Date = b.AppointmentDate,
                ClientName = $"{b.Customer?.FirstName} {b.Customer?.LastName}",
                ServiceName = b.UserService?.Service?.Name ?? "Service",
                OriginalPrice = b.TotalAmount,                 // keep the original total for reference
                YourEarnings = GetTotalPaid(b) * (1 - _commissionRate),
                PlatformFee = GetTotalPaid(b) * _commissionRate,
                TipAmount = 0m,                                // you can later add tip tracking
                Status = b.Status.ToString()
                DepositPaid = b.DepositPaid,
                FinalPaymentPaid = b.FinalPaymentPaid,
                IsFullyPaid = (b.DepositPaid + b.FinalPaymentPaid) >= b.TotalAmount
            }).ToList();

            var model = new ArtistEarningsViewModel
            {
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
                History = history

            };

            return View(model);
        }

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
            csv.AppendLine("Date,Client,Service,Original Price,Your Cut,Platform Fee,Tip,Status");

            foreach (var b in bookings)
            {
                var clientName = $"{b.Customer?.FirstName} {b.Customer?.LastName}".Replace("\"", "\"\"");
                var serviceName = (b.UserService?.Service?.Name ?? "Service").Replace("\"", "\"\"");
                decimal totalPaid = b.DepositPaid + b.FinalPaymentPaid; // actual money received
                decimal yourCut = totalPaid * (1 - _commissionRate);
                decimal platformFee = totalPaid * _commissionRate;

                csv.AppendLine($"\"{b.AppointmentDate:dd/MM/yyyy}\",\"{clientName}\",\"{serviceName}\",\"R{b.TotalAmount:N2}\",\"R{yourCut:N2}\",\"R{platformFee:N2}\",\"R0.00\",\"{b.Status}\"");
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