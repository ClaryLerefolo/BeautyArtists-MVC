using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml; // Required: Install-Package EPPlus
using OfficeOpenXml.Style;
using System.ComponentModel;
using System.Text;

namespace BeautyArtists.Controllers
{
    [Authorize(Roles = "Admin")]
    public class RevenueController : Controller
    {
        private readonly ApplicationDbContext _context;
        private const decimal COMMISSION_RATE = 0.15m;
        private const decimal BOOKING_FEE = 5.00m;

        public RevenueController(ApplicationDbContext context)
        {
            _context = context;
            // Set License for EPPlus (Excel Library) - KEEP AS IS
            // OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
        }

        // ── SHARED: build filtered bookings ──
        private async Task<List<Booking>> GetFilteredBookings(
            string? filterProvince, string? filterArtistId,
            string? filterStatus, string? filterServiceId,
            DateTime? filterFrom, DateTime? filterTo)
        {
            var query = _context.Bookings
                .Include(b => b.Customer)
                .Include(b => b.UserService).ThenInclude(us => us.Service)
                .Include(b => b.UserService).ThenInclude(us => us.Artist)
                    .ThenInclude(a => a.ArtistProfile)
                .AsQueryable();

            if (!string.IsNullOrEmpty(filterProvince))
                query = query.Where(b => b.UserService.Artist.ArtistProfile.Province == filterProvince);

            if (!string.IsNullOrEmpty(filterArtistId))
                query = query.Where(b => b.UserService.ArtistId == filterArtistId);

            if (!string.IsNullOrEmpty(filterServiceId) && int.TryParse(filterServiceId, out int svcId))
                query = query.Where(b => b.UserService.ServiceId == svcId);

            if (!string.IsNullOrEmpty(filterStatus) &&
                Enum.TryParse<Booking.BookingStatus>(filterStatus, out var statusEnum))
                query = query.Where(b => b.Status == statusEnum);

            if (filterFrom.HasValue)
                query = query.Where(b => b.AppointmentDate >= filterFrom.Value);

            if (filterTo.HasValue)
                query = query.Where(b => b.AppointmentDate <= filterTo.Value.AddDays(1));

            return await query.OrderByDescending(b => b.AppointmentDate).ToListAsync();
        }

        // ── MAP to report items ──
        private List<BookingReportItem> MapToReportItems(List<Booking> bookings)
        {
            return bookings.Select(b => new BookingReportItem
            {
                BookingId = b.Id,
                AppointmentDate = b.AppointmentDate,
                ClientName = $"{b.Customer?.FirstName} {b.Customer?.LastName}".Trim(),
                ArtistName = !string.IsNullOrEmpty(b.UserService?.Artist?.FirstName)
                    ? $"{b.UserService.Artist.FirstName} {b.UserService.Artist.LastName}".Trim()
                    : b.UserService?.Artist?.UserName ?? "—",
                ServiceName = b.UserService?.Service?.Name ?? "—",
                Province = b.UserService?.Artist?.ArtistProfile?.Province ?? "—",
                Status = b.Status.ToString(),
                // 🔥 FIX: Artist's 85% cut for completed bookings
                Amount = b.Status == Booking.BookingStatus.Completed
                    ? b.ServicePrice * (1 - COMMISSION_RATE)
                    : 0m,
                // 🔥 NEW: Detailed breakdown for admin
                ServicePrice = b.ServicePrice,
                BookingFee = b.BookingFee,
                ClientTotal = b.TotalAmount,
                PlatformCommission = b.ServicePrice * COMMISSION_RATE,
                ArtistNet = b.ServicePrice * (1 - COMMISSION_RATE)
            }).ToList();
        }

        // ── POPULATE DROPDOWNS ──
        private async Task PopulateDropdowns(List<Booking> allBookings)
        {
            ViewBag.Provinces = allBookings
                .Select(b => b.UserService?.Artist?.ArtistProfile?.Province)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct().OrderBy(p => p).ToList();

            ViewBag.Artists = await _context.Users
                .Where(u => _context.UserServices.Any(us => us.ArtistId == u.Id))
                .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName })
                .ToListAsync();

            ViewBag.Services = await _context.Services
                .Select(s => new { s.Id, s.Name })
                .OrderBy(s => s.Name).ToListAsync();
        }

        // ══════════════════════════════════
        //  GET: Revenue/Index
        // ══════════════════════════════════
        public async Task<IActionResult> Index(
            string? filterProvince, string? filterArtistId,
            string? filterStatus, string? filterServiceId,
            DateTime? filterFrom, DateTime? filterTo)
        {
            var filtered = await GetFilteredBookings(
                filterProvince, filterArtistId, filterStatus,
                filterServiceId, filterFrom, filterTo);

            var allBookings = await _context.Bookings
                .Include(b => b.UserService).ThenInclude(us => us.Artist)
                    .ThenInclude(a => a.ArtistProfile)
                .ToListAsync();

            var now = DateTime.Now;

            var model = new RevenueViewModel
            {
                FilterProvince = filterProvince,
                FilterArtistId = filterArtistId,
                FilterStatus = filterStatus,
                FilterServiceId = filterServiceId,
                FilterFrom = filterFrom,
                FilterTo = filterTo,

                // 🔥 FIX: Artist's 85% cut for completed bookings
                TotalRevenue = allBookings
                    .Where(b => b.Status == Booking.BookingStatus.Completed)
                    .Sum(b => b.ServicePrice * (1 - COMMISSION_RATE)),

                MonthRevenue = allBookings
                    .Where(b => b.Status == Booking.BookingStatus.Completed
                             && b.AppointmentDate.Month == now.Month
                             && b.AppointmentDate.Year == now.Year)
                    .Sum(b => b.ServicePrice * (1 - COMMISSION_RATE)),

                WeekRevenue = allBookings
                    .Where(b => b.Status == Booking.BookingStatus.Completed
                             && b.AppointmentDate >= now.AddDays(-7))
                    .Sum(b => b.ServicePrice * (1 - COMMISSION_RATE)),

                TotalBookings = allBookings.Count,
                CompletedBookings = allBookings.Count(b => b.Status == Booking.BookingStatus.Completed),

                // 🔥 FIX: Top Services use artist's 85% cut
                TopServices = filtered
                    .GroupBy(b => b.UserService?.Service?.Name ?? "Unknown")
                    .Select(g => new ServiceRevenueItem
                    {
                        ServiceName = g.Key,
                        BookingCount = g.Count(),
                        TotalRevenue = g.Where(b => b.Status == Booking.BookingStatus.Completed)
                                        .Sum(b => b.ServicePrice * (1 - COMMISSION_RATE))
                    })
                    .OrderByDescending(s => s.TotalRevenue).Take(8).ToList(),

                // 🔥 FIX: Top Artists use artist's 85% cut
                TopArtists = filtered
                    .GroupBy(b => b.UserService?.ArtistId ?? "unknown")
                    .Select(g => new ArtistRevenueItem
                    {
                        ArtistName = !string.IsNullOrEmpty(g.First().UserService?.Artist?.FirstName)
                            ? $"{g.First().UserService.Artist.FirstName} {g.First().UserService.Artist.LastName}".Trim()
                            : g.First().UserService?.Artist?.UserName ?? "Unknown",
                        Province = g.First().UserService?.Artist?.ArtistProfile?.Province ?? "—",
                        BookingCount = g.Count(),
                        TotalRevenue = g.Where(b => b.Status == Booking.BookingStatus.Completed)
                                        .Sum(b => b.ServicePrice * (1 - COMMISSION_RATE))
                    })
                    .OrderByDescending(a => a.TotalRevenue).Take(8).ToList(),

                // 🔥 FIX: Bookings by Province use artist's 85% cut
                BookingsByProvince = filtered
                    .GroupBy(b => b.UserService?.Artist?.ArtistProfile?.Province ?? "Unknown")
                    .Select(g => new ProvinceBookingItem
                    {
                        Province = g.Key,
                        BookingCount = g.Count(),
                        TotalRevenue = g.Where(b => b.Status == Booking.BookingStatus.Completed)
                                        .Sum(b => b.ServicePrice * (1 - COMMISSION_RATE))
                    })
                    .OrderByDescending(p => p.TotalRevenue).ToList(),

                // 🔥 FIX: Monthly Trend use artist's 85% cut
                MonthlyTrend = allBookings
                    .Where(b => b.AppointmentDate >= now.AddMonths(-11))
                    .GroupBy(b => new { b.AppointmentDate.Year, b.AppointmentDate.Month })
                    .Select(g => new MonthlyRevenueItem
                    {
                        Month = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                        BookingCount = g.Count(),
                        TotalRevenue = g.Where(b => b.Status == Booking.BookingStatus.Completed)
                                        .Sum(b => b.ServicePrice * (1 - COMMISSION_RATE))
                    })
                    .OrderBy(m => m.Month).ToList(),

                FilteredBookings = MapToReportItems(filtered)
            };

            await PopulateDropdowns(allBookings);
            return View(model);
        }

        // ══════════════════════════════════
        //  GET: Revenue/DownloadCsv
        // ══════════════════════════════════
        public async Task<IActionResult> DownloadCsv(
            string? filterProvince, string? filterArtistId,
            string? filterStatus, string? filterServiceId,
            DateTime? filterFrom, DateTime? filterTo)
        {
            var bookings = await GetFilteredBookings(
                filterProvince, filterArtistId, filterStatus,
                filterServiceId, filterFrom, filterTo);

            var items = MapToReportItems(bookings);
            var sb = new StringBuilder();

            sb.AppendLine("BEAUTY IN RED AND GOLD — REVENUE REPORT");
            sb.AppendLine($"Generated:,{DateTime.Now:dd MMM yyyy HH:mm}");

            var filters = new List<string>();
            if (!string.IsNullOrEmpty(filterProvince)) filters.Add($"Province: {filterProvince}");
            if (!string.IsNullOrEmpty(filterStatus)) filters.Add($"Status: {filterStatus}");
            if (filterFrom.HasValue) filters.Add($"From: {filterFrom.Value:dd MMM yyyy}");
            if (filterTo.HasValue) filters.Add($"To: {filterTo.Value:dd MMM yyyy}");

            sb.AppendLine($"Filters:,\"{(filters.Any() ? string.Join(" | ", filters) : "None")}\"");
            sb.AppendLine($"Total Records:,{items.Count}");
            sb.AppendLine($"Total Artist Revenue:,R {items.Sum(i => i.ArtistNet):N2}");
            sb.AppendLine($"Total Platform Commission:,R {items.Sum(i => i.PlatformCommission):N2}");
            sb.AppendLine($"Total Booking Fees:,R {items.Sum(i => i.BookingFee):N2}");
            sb.AppendLine();
            sb.AppendLine("Booking ID,Date,Time,Client,Artist,Service,Province,Status,Service Price,Booking Fee,Client Total,Artist Net (85%),Platform Commission (15%)");

            foreach (var item in items)
            {
                sb.AppendLine($"{item.BookingId},\"{item.AppointmentDate:dd MMM yyyy}\",\"{item.AppointmentDate:HH:mm}\",\"{item.ClientName}\",\"{item.ArtistName}\",\"{item.ServiceName}\",\"{item.Province}\",{item.Status},{item.ServicePrice:N2},{item.BookingFee:N2},{item.ClientTotal:N2},{item.ArtistNet:N2},{item.PlatformCommission:N2}");
            }

            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"Report_{DateTime.Now:yyyyMMdd}.csv");
        }

        // ══════════════════════════════════
        //  GET: Revenue/DownloadExcel
        // ══════════════════════════════════
        public async Task<IActionResult> DownloadExcel(
            string? filterProvince, string? filterArtistId,
            string? filterStatus, string? filterServiceId,
            DateTime? filterFrom, DateTime? filterTo)
        {
            var bookings = await GetFilteredBookings(filterProvince, filterArtistId, filterStatus, filterServiceId, filterFrom, filterTo);
            var items = MapToReportItems(bookings);

            OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
            using (var package = new ExcelPackage())
            {
                var sheet = package.Workbook.Worksheets.Add("Revenue Report");

                // Headers & Styling
                sheet.Cells["A1"].Value = "BEAUTY IN RED AND GOLD — REVENUE REPORT";
                sheet.Cells["A1:N1"].Merge = true;
                sheet.Cells["A1"].Style.Font.Bold = true;
                sheet.Cells["A1"].Style.Font.Size = 16;

                sheet.Cells["A2"].Value = $"Generated: {DateTime.Now:dd MMM yyyy HH:mm}";

                // Column Headers
                string[] headers = { "Booking ID", "Date", "Time", "Client", "Artist", "Service", "Province", "Status", "Service Price", "Booking Fee", "Client Total", "Artist Net (85%)", "Platform Commission (15%)" };
                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = sheet.Cells[4, i + 1];
                    cell.Value = headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.Gold);
                }

                int row = 5;
                foreach (var item in items)
                {
                    sheet.Cells[row, 1].Value = item.BookingId;
                    sheet.Cells[row, 2].Value = item.AppointmentDate.ToString("dd MMM yyyy");
                    sheet.Cells[row, 3].Value = item.AppointmentDate.ToString("HH:mm");
                    sheet.Cells[row, 4].Value = item.ClientName;
                    sheet.Cells[row, 5].Value = item.ArtistName;
                    sheet.Cells[row, 6].Value = item.ServiceName;
                    sheet.Cells[row, 7].Value = item.Province;
                    sheet.Cells[row, 8].Value = item.Status;
                    sheet.Cells[row, 9].Value = item.ServicePrice;
                    sheet.Cells[row, 9].Style.Numberformat.Format = "R #,##0.00";
                    sheet.Cells[row, 10].Value = item.BookingFee;
                    sheet.Cells[row, 10].Style.Numberformat.Format = "R #,##0.00";
                    sheet.Cells[row, 11].Value = item.ClientTotal;
                    sheet.Cells[row, 11].Style.Numberformat.Format = "R #,##0.00";
                    sheet.Cells[row, 12].Value = item.ArtistNet;
                    sheet.Cells[row, 12].Style.Numberformat.Format = "R #,##0.00";
                    sheet.Cells[row, 13].Value = item.PlatformCommission;
                    sheet.Cells[row, 13].Style.Numberformat.Format = "R #,##0.00";
                    row++;
                }

                sheet.Cells.AutoFitColumns();
                return File(package.GetAsByteArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Report_{DateTime.Now:yyyyMMdd}.xlsx");
            }
        }

        // ══════════════════════════════════
        //  GET: Revenue/DownloadWord
        // ══════════════════════════════════
        public async Task<IActionResult> DownloadWord(
            string? filterProvince, string? filterArtistId,
            string? filterStatus, string? filterServiceId,
            DateTime? filterFrom, DateTime? filterTo)
        {
            var bookings = await GetFilteredBookings(filterProvince, filterArtistId, filterStatus, filterServiceId, filterFrom, filterTo);
            var items = MapToReportItems(bookings);

            var sb = new StringBuilder();
            sb.Append("<html><body style='font-family:Arial;'>");
            sb.Append("<h1 style='color:#b30000;'>BEAUTY IN RED AND GOLD</h1>");
            sb.Append($"<p><b>Report Generated:</b> {DateTime.Now:dd MMM yyyy HH:mm}</p>");
            sb.Append("<table border='1' cellspacing='0' cellpadding='5' style='width:100%; border-collapse:collapse;'>");
            sb.Append("<tr style='background-color:gold;'><th>ID</th><th>Date</th><th>Client</th><th>Artist</th><th>Service</th><th>Service Price</th><th>Booking Fee</th><th>Artist Net</th></tr>");

            foreach (var item in items)
            {
                sb.Append($"<tr><td>{item.BookingId}</td><td>{item.AppointmentDate:dd MMM yyyy}</td><td>{item.ClientName}</td><td>{item.ArtistName}</td><td>{item.ServiceName}</td><td>R {item.ServicePrice:N2}</td><td>R {item.BookingFee:N2}</td><td>R {item.ArtistNet:N2}</td></tr>");
            }

            sb.Append("</table>");
            sb.Append($"<br/><p><b>Total Artist Revenue:</b> R {items.Where(i => i.Status == "Completed").Sum(i => i.ArtistNet):N2}</p>");
            sb.Append($"<p><b>Total Platform Commission:</b> R {items.Where(i => i.Status == "Completed").Sum(i => i.PlatformCommission):N2}</p>");
            sb.Append($"<p><b>Total Booking Fees:</b> R {items.Where(i => i.Status == "Completed").Sum(i => i.BookingFee):N2}</p>");
            sb.Append("</body></html>");

            return File(Encoding.UTF8.GetBytes(sb.ToString()), "application/msword", $"Report_{DateTime.Now:yyyyMMdd}.doc");
        }

        public async Task<IActionResult> DownloadPdf(string? filterProvince, string? filterArtistId, string? filterStatus, string? filterServiceId, DateTime? filterFrom, DateTime? filterTo)
        {
            var bookings = await GetFilteredBookings(filterProvince, filterArtistId, filterStatus, filterServiceId, filterFrom, filterTo);
            var items = MapToReportItems(bookings);

            var sb = new StringBuilder();
            sb.Append("<div style='text-align:center; font-family:sans-serif;'>");
            sb.Append("<h1 style='color:red;'>BEAUTY IN RED AND GOLD</h1><h2>Revenue Report</h2>");
            sb.Append("<table style='width:100%; border:1px solid black; border-collapse:collapse;'>");
            sb.Append("<tr style='background-color:gold;'><th>Date</th><th>Client</th><th>Service</th><th>Service Price</th><th>Booking Fee</th><th>Artist Net</th></tr>");
            foreach (var item in items)
            {
                sb.Append($"<tr><td style='border:1px solid black;'>{item.AppointmentDate:dd/MM/yyyy}</td><td style='border:1px solid black;'>{item.ClientName}</td><td style='border:1px solid black;'>{item.ServiceName}</td><td style='border:1px solid black;'>R {item.ServicePrice:N2}</td><td style='border:1px solid black;'>R {item.BookingFee:N2}</td><td style='border:1px solid black;'>R {item.ArtistNet:N2}</td></tr>");
            }
            sb.Append("</table></div>");

            return File(Encoding.UTF8.GetBytes(sb.ToString()), "application/pdf", $"Report_{DateTime.Now:yyyyMMdd}.pdf");
        }
    }
}