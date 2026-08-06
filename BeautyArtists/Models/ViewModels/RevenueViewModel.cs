namespace BeautyArtists.Models.ViewModels
{
    public class RevenueViewModel
    {
        public decimal TotalRevenue { get; set; }
        public decimal MonthRevenue { get; set; }
        public decimal WeekRevenue { get; set; }
        public int TotalBookings { get; set; }
        public int CompletedBookings { get; set; }

        public string FilterProvince { get; set; }
        public string? FilterArtistId { get; set; }
        public string? FilterServiceId { get; set; }
        public string FilterStatus { get; set; }
        public DateTime? FilterFrom { get; set; }
        public DateTime? FilterTo { get; set; }

        public List<ServiceRevenueItem> TopServices { get; set; } = new();
        public List<ArtistRevenueItem> TopArtists { get; set; } = new();
        public List<ProvinceBookingItem> BookingsByProvince { get; set; } = new();
        public List<MonthlyRevenueItem> MonthlyTrend { get; set; } = new();
        public List<BookingReportItem> FilteredBookings { get; set; } = new();
    }

    // Most booked services
    public class ServiceRevenueItem
    {
        public string ServiceName { get; set; } = "";
        public int BookingCount { get; set; }
        public decimal TotalRevenue { get; set; }
    }

    // Most Booked Artists
    public class ArtistRevenueItem
    {
        public string ArtistName { get; set; } = "";
        public string Province { get; set; } = "";
        public int BookingCount { get; set; }
        public decimal TotalRevenue { get; set; }
    }

    // Booking by Province
    public class ProvinceBookingItem
    {
        public string Province { get; set; } = "";
        public int BookingCount { get; set; }
        public decimal TotalRevenue { get; set; }
    }

    // Monthly Revenue Trend
    public class MonthlyRevenueItem
    {
        public string Month { get; set; } = "";
        public int BookingCount { get; set; }
        public decimal TotalRevenue { get; set; }
    }

    // ✅ FIXED: Individual booking row for the report table
    public class BookingReportItem
    {
        public int BookingId { get; set; }
        public DateTime AppointmentDate { get; set; }
        public string ClientName { get; set; } = "";
        public string ArtistName { get; set; } = "";
        public string ServiceName { get; set; } = "";
        public string Province { get; set; } = "";
        public string Status { get; set; } = "";
        public decimal Amount { get; set; }

        // ✅ CORRECT PRICING BREAKDOWN
        public decimal ServicePrice { get; set; }          // Artist's price (100%)
        public decimal BookingFee { get; set; }            // R5 (platform earns)
        public decimal ClientTotal { get; set; }           // ServicePrice * 1.15 + BookingFee
        public decimal PlatformMarkup { get; set; }        // 15% of ServicePrice (added to client)
        public decimal ArtistNet { get; set; }             // Artist gets 100% (ServicePrice)
        public decimal PlatformEarnings { get; set; }      // PlatformMarkup + BookingFee
    }
}