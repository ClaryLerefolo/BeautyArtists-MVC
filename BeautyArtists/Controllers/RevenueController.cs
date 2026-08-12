using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.ComponentModel;
using System.Text;

namespace BeautyArtists.Controllers
{
    [Authorize(Roles = "Admin")]
    public class RevenueController : Controller
    {
        private readonly ApplicationDbContext _context;

        // ─── ✅ NEW FEE STRUCTURE ───
        private const decimal CLIENT_MARKUP = 0.04m;      // 4% added to client
        private const decimal BOOKING_FEE = 5.00m;        // R5 booking fee
        private const decimal COMMISSION_NEW = 0.10m;      // 10% for new clients
        private const decimal COMMISSION_REPEAT = 15.00m;  // R15 for repeat clients
        private const decimal MIN_COMMISSION = 8.00m;      // Minimum R8 safeguard

        public RevenueController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ─── HELPER: Check if client is new ───
        private async Task<bool> IsNewClient(string artistId, string customerId)
        {
            var existingBookings = await _context.Bookings
                .Where(b => b.UserService.ArtistId == artistId
                       && b.CustomerId == customerId
                       && b.Status == Booking.BookingStatus.Completed)
                .AnyAsync();

            return !existingBookings;
        }

        // ─── HELPER: Calculate platform fee (10% or R15) ───
        private decimal GetPlatformFee(decimal servicePrice, bool isNewClient)
        {
            // NEW client: 10% of artist price (no cap)
            // REPEAT client: R15 flat fee
            var platformFee = isNewClient ? servicePrice * COMMISSION_NEW : COMMISSION_REPEAT;

            // Safeguard: Minimum R8
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

        // ─── HELPER: Calculate client total ───
        private decimal GetClientTotal(decimal servicePrice, bool isNewClient)
        {
            // Client pays: Artist Price + 4% markup + R5 booking fee
            var markedUpPrice = servicePrice + (servicePrice * CLIENT_MARKUP);
            return markedUpPrice + BOOKING_FEE;
        }

        // ─── SHARED: build filtered bookings ───
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

        // ─── MAP to report items using ViewModel ───
        private async Task<List<BookingReportItem>> MapToReportItems(List<Booking> bookings)
        {
            var items = new List<BookingReportItem>();

            foreach (var b in bookings)
            {
                var isNew = await IsNewClient(b.UserService.ArtistId, b.CustomerId);
                var platformFee = GetPlatformFee(b.ServicePrice, isNew);
                var artistPayout = GetArtistPayout(b.ServicePrice, isNew);
                var clientTotal = GetClientTotal(b.ServicePrice, isNew);
                var markupAmount = b.ServicePrice * CLIENT_MARKUP;

                items.Add(new BookingReportItem
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
                    ClientType = isNew ? "New" : "Repeat",

                    // ─── NEW BREAKDOWN ───
                    ServicePrice = b.ServicePrice,                      // Artist's price (100%)
                    ClientMarkup = markupAmount,                        // 4% markup (platform earns)
                    BookingFee = BOOKING_FEE,                           // R5 (platform earns)
                    ClientTotal = clientTotal,                          // What client pays
                    PlatformFee = platformFee,                          // 10% or R15 (platform earns)
                    ArtistNet = artistPayout,                           // What artist actually gets

                    // ─── PLATFORM EARNINGS ───
                    PlatformEarnings = markupAmount + platformFee + BOOKING_FEE,

                    // ─── For backward compatibility ───
                    Amount = b.Status == Booking.BookingStatus.Completed ? artistPayout : 0m
                });
            }

            return items;
        }

        // ─── POPULATE DROPDOWNS ───
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
                .Include(b => b.Customer)
                .ToListAsync();

            var now = DateTime.Now;
            var completed = allBookings.Where(b => b.Status == Booking.BookingStatus.Completed).ToList();

            // ─── CALCULATE EARNINGS ───
            decimal totalArtistPayout = 0m;
            decimal totalPlatformEarnings = 0m;
            decimal totalClientPaid = 0m;
            decimal totalMarkup = 0m;
            decimal totalBookingFees = 0m;
            decimal totalPlatformFees = 0m;

            foreach (var b in completed)
            {
                var isNew = await IsNewClient(b.UserService.ArtistId, b.CustomerId);
                totalArtistPayout += GetArtistPayout(b.ServicePrice, isNew);
                totalClientPaid += GetClientTotal(b.ServicePrice, isNew);
                totalMarkup += b.ServicePrice * CLIENT_MARKUP;
                totalBookingFees += BOOKING_FEE;
                totalPlatformFees += GetPlatformFee(b.ServicePrice, isNew);
                totalPlatformEarnings += (b.ServicePrice * CLIENT_MARKUP) + GetPlatformFee(b.ServicePrice, isNew) + BOOKING_FEE;
            }

            // ─── MONTHLY BREAKDOWN ───
            decimal monthArtistPayout = 0m;
            foreach (var b in completed.Where(b => b.AppointmentDate.Month == now.Month && b.AppointmentDate.Year == now.Year))
            {
                var isNew = await IsNewClient(b.UserService.ArtistId, b.CustomerId);
                monthArtistPayout += GetArtistPayout(b.ServicePrice, isNew);
            }

            decimal weekArtistPayout = 0m;
            foreach (var b in completed.Where(b => b.AppointmentDate >= now.AddDays(-7)))
            {
                var isNew = await IsNewClient(b.UserService.ArtistId, b.CustomerId);
                weekArtistPayout += GetArtistPayout(b.ServicePrice, isNew);
            }

            // ─── BUILD MODEL ───
            var model = new RevenueViewModel
            {
                FilterProvince = filterProvince,
                FilterArtistId = filterArtistId,
                FilterStatus = filterStatus,
                FilterServiceId = filterServiceId,
                FilterFrom = filterFrom,
                FilterTo = filterTo,

                // ─── REVENUE TOTALS ───
                TotalRevenue = totalArtistPayout,
                MonthRevenue = monthArtistPayout,
                WeekRevenue = weekArtistPayout,

                // ─── PLATFORM EARNINGS ───
                TotalPlatformEarnings = totalPlatformEarnings,
                TotalClientPaid = totalClientPaid,
                TotalMarkupEarned = totalMarkup,
                TotalBookingFeesEarned = totalBookingFees,
                TotalCommissionEarned = totalPlatformFees,

                TotalBookings = allBookings.Count,
                CompletedBookings = completed.Count,

                // ─── TOP SERVICES ───
                TopServices = filtered
                    .GroupBy(b => b.UserService?.Service?.Name ?? "Unknown")
                    .Select(g => new ServiceRevenueItem
                    {
                        ServiceName = g.Key,
                        BookingCount = g.Count(),
                        TotalRevenue = g.Where(b => b.Status == Booking.BookingStatus.Completed)
                                        .Sum(b => b.ServicePrice)
                    })
                    .OrderByDescending(s => s.TotalRevenue).Take(8).ToList(),

                // ─── TOP ARTISTS ───
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
                                        .Sum(b => b.ServicePrice)
                    })
                    .OrderByDescending(a => a.TotalRevenue).Take(8).ToList(),

                // ─── BOOKINGS BY PROVINCE ───
                BookingsByProvince = filtered
                    .GroupBy(b => b.UserService?.Artist?.ArtistProfile?.Province ?? "Unknown")
                    .Select(g => new ProvinceBookingItem
                    {
                        Province = g.Key,
                        BookingCount = g.Count(),
                        TotalRevenue = g.Where(b => b.Status == Booking.BookingStatus.Completed)
                                        .Sum(b => b.ServicePrice)
                    })
                    .OrderByDescending(p => p.TotalRevenue).ToList(),

                // ─── MONTHLY TREND ───
                MonthlyTrend = allBookings
                    .Where(b => b.AppointmentDate >= now.AddMonths(-11))
                    .GroupBy(b => new { b.AppointmentDate.Year, b.AppointmentDate.Month })
                    .Select(g => new MonthlyRevenueItem
                    {
                        Month = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                        BookingCount = g.Count(),
                        TotalRevenue = g.Where(b => b.Status == Booking.BookingStatus.Completed)
                                        .Sum(b => b.ServicePrice)
                    })
                    .OrderBy(m => m.Month).ToList(),

                FilteredBookings = await MapToReportItems(filtered)
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

            var items = await MapToReportItems(bookings);
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
            sb.AppendLine($"Total Artist Payout:,R {items.Sum(i => i.ArtistNet):N2}");
            sb.AppendLine($"Total Client Markup (4%):,R {items.Sum(i => i.ClientMarkup):N2}");
            sb.AppendLine($"Total Platform Fee (10%/R15):,R {items.Sum(i => i.PlatformFee):N2}");
            sb.AppendLine($"Total Booking Fees:,R {items.Sum(i => i.BookingFee):N2}");
            sb.AppendLine($"Total Platform Earnings:,R {items.Sum(i => i.PlatformEarnings):N2}");
            sb.AppendLine($"Total Client Paid:,R {items.Sum(i => i.ClientTotal):N2}");
            sb.AppendLine();
            sb.AppendLine("Booking ID,Date,Time,Client,Artist,Service,Province,Status,Client Type,Service Price,4% Markup,Platform Fee (10%/R15),Booking Fee (R5),Client Total,Artist Net,Platform Earnings");

            foreach (var item in items)
            {
                sb.AppendLine($"{item.BookingId},\"{item.AppointmentDate:dd MMM yyyy}\",\"{item.AppointmentDate:HH:mm}\",\"{item.ClientName}\",\"{item.ArtistName}\",\"{item.ServiceName}\",\"{item.Province}\",{item.Status},{item.ClientType},{item.ServicePrice:N2},{item.ClientMarkup:N2},{item.PlatformFee:N2},{item.BookingFee:N2},{item.ClientTotal:N2},{item.ArtistNet:N2},{item.PlatformEarnings:N2}");
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
            var items = await MapToReportItems(bookings);

            OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
            using (var package = new ExcelPackage())
            {
                var sheet = package.Workbook.Worksheets.Add("Revenue Report");

                // Headers & Styling
                sheet.Cells["A1"].Value = "BEAUTY IN RED AND GOLD — REVENUE REPORT";
                sheet.Cells["A1:O1"].Merge = true;
                sheet.Cells["A1"].Style.Font.Bold = true;
                sheet.Cells["A1"].Style.Font.Size = 16;

                sheet.Cells["A2"].Value = $"Generated: {DateTime.Now:dd MMM yyyy HH:mm}";

                // Column Headers
                string[] headers = {
                    "Booking ID", "Date", "Time", "Client", "Artist", "Service",
                    "Province", "Status", "Client Type", "Service Price",
                    "4% Markup", "Platform Fee", "Booking Fee (R5)",
                    "Client Total", "Artist Net", "Platform Earnings"
                };
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
                    sheet.Cells[row, 9].Value = item.ClientType;
                    sheet.Cells[row, 10].Value = item.ServicePrice;
                    sheet.Cells[row, 10].Style.Numberformat.Format = "R #,##0.00";
                    sheet.Cells[row, 11].Value = item.ClientMarkup;
                    sheet.Cells[row, 11].Style.Numberformat.Format = "R #,##0.00";
                    sheet.Cells[row, 12].Value = item.PlatformFee;
                    sheet.Cells[row, 12].Style.Numberformat.Format = "R #,##0.00";
                    sheet.Cells[row, 13].Value = item.BookingFee;
                    sheet.Cells[row, 13].Style.Numberformat.Format = "R #,##0.00";
                    sheet.Cells[row, 14].Value = item.ClientTotal;
                    sheet.Cells[row, 14].Style.Numberformat.Format = "R #,##0.00";
                    sheet.Cells[row, 15].Value = item.ArtistNet;
                    sheet.Cells[row, 15].Style.Numberformat.Format = "R #,##0.00";
                    sheet.Cells[row, 16].Value = item.PlatformEarnings;
                    sheet.Cells[row, 16].Style.Numberformat.Format = "R #,##0.00";
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
            var items = await MapToReportItems(bookings);

            var sb = new StringBuilder();
            sb.Append("<html><body style='font-family:Arial;'>");
            sb.Append("<h1 style='color:#b30000;'>BEAUTY IN RED AND GOLD</h1>");
            sb.Append($"<p><b>Report Generated:</b> {DateTime.Now:dd MMM yyyy HH:mm}</p>");
            sb.Append("<table border='1' cellspacing='0' cellpadding='5' style='width:100%; border-collapse:collapse;'>");
            sb.Append("<tr style='background-color:gold;'><th>ID</th><th>Date</th><th>Client</th><th>Artist</th><th>Service</th><th>Type</th><th>Service Price</th><th>4% Markup</th><th>Platform Fee</th><th>Booking Fee</th><th>Artist Net</th><th>Platform Earnings</th></tr>");

            foreach (var item in items)
            {
                sb.Append($"<tr><td>{item.BookingId}</td><td>{item.AppointmentDate:dd MMM yyyy}</td><td>{item.ClientName}</td><td>{item.ArtistName}</td><td>{item.ServiceName}</td><td>{item.ClientType}</td><td>R {item.ServicePrice:N2}</td><td>R {item.ClientMarkup:N2}</td><td>R {item.PlatformFee:N2}</td><td>R {item.BookingFee:N2}</td><td>R {item.ArtistNet:N2}</td><td>R {item.PlatformEarnings:N2}</td></tr>");
            }

            sb.Append("</table>");
            sb.Append($"<br/><p><b>Total Artist Payout:</b> R {items.Where(i => i.Status == "Completed").Sum(i => i.ArtistNet):N2}</p>");
            sb.Append($"<p><b>Total Platform Fees (4% + 10%/R15):</b> R {items.Where(i => i.Status == "Completed").Sum(i => i.PlatformEarnings):N2}</p>");
            sb.Append($"<p><b>Total Booking Fees:</b> R {items.Where(i => i.Status == "Completed").Sum(i => i.BookingFee):N2}</p>");
            sb.Append($"<p><b>Total Client Paid:</b> R {items.Where(i => i.Status == "Completed").Sum(i => i.ClientTotal):N2}</p>");
            sb.Append("</body></html>");

            return File(Encoding.UTF8.GetBytes(sb.ToString()), "application/msword", $"Report_{DateTime.Now:yyyyMMdd}.doc");
        }

        public async Task<IActionResult> DownloadPdf(string? filterProvince, string? filterArtistId, string? filterStatus, string? filterServiceId, DateTime? filterFrom, DateTime? filterTo)
        {
            var bookings = await GetFilteredBookings(filterProvince, filterArtistId, filterStatus, filterServiceId, filterFrom, filterTo);
            var items = await MapToReportItems(bookings);

            var sb = new StringBuilder();
            sb.Append("<div style='text-align:center; font-family:sans-serif;'>");
            sb.Append("<h1 style='color:red;'>BEAUTY IN RED AND GOLD</h1><h2>Revenue Report</h2>");
            sb.Append("<table style='width:100%; border:1px solid black; border-collapse:collapse;'>");
            sb.Append("<tr style='background-color:gold;'><th>Date</th><th>Client</th><th>Service</th><th>Type</th><th>Service Price</th><th>4% Markup</th><th>Platform Fee</th><th>Booking Fee</th><th>Artist Net</th><th>Platform Earnings</th></tr>");
            foreach (var item in items)
            {
                sb.Append($"<tr><td style='border:1px solid black;'>{item.AppointmentDate:dd/MM/yyyy}</td><td style='border:1px solid black;'>{item.ClientName}</td><td style='border:1px solid black;'>{item.ServiceName}</td><td style='border:1px solid black;'>{item.ClientType}</td><td style='border:1px solid black;'>R {item.ServicePrice:N2}</td><td style='border:1px solid black;'>R {item.ClientMarkup:N2}</td><td style='border:1px solid black;'>R {item.PlatformFee:N2}</td><td style='border:1px solid black;'>R {item.BookingFee:N2}</td><td style='border:1px solid black;'>R {item.ArtistNet:N2}</td><td style='border:1px solid black;'>R {item.PlatformEarnings:N2}</td></tr>");
            }
            sb.Append("</table></div>");

            return File(Encoding.UTF8.GetBytes(sb.ToString()), "application/pdf", $"Report_{DateTime.Now:yyyyMMdd}.pdf");
        }
    }
}