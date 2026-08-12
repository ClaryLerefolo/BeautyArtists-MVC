namespace BeautyArtists.Models.ViewModels
{
    public class RevenueViewModel
    {
        // ─── REVENUE TOTALS ───
        public decimal TotalRevenue { get; set; }              // Artist payouts
        public decimal MonthRevenue { get; set; }              // Artist payouts this month
        public decimal WeekRevenue { get; set; }               // Artist payouts this week
        public int TotalBookings { get; set; }
        public int CompletedBookings { get; set; }

        // ─── ✅ NEW: PLATFORM EARNINGS BREAKDOWN ───
        public decimal TotalPlatformEarnings { get; set; }     // Total platform revenue
        public decimal TotalClientPaid { get; set; }           // Total paid by clients
        public decimal TotalMarkupEarned { get; set; }         // 4% markup on artist price
        public decimal TotalBookingFeesEarned { get; set; }    // R5 per booking
        public decimal TotalCommissionEarned { get; set; }     // 10% or R15 per booking

        // ─── FILTERS ───
        public string FilterProvince { get; set; } = "";
        public string? FilterArtistId { get; set; }
        public string? FilterServiceId { get; set; }
        public string FilterStatus { get; set; } = "";
        public DateTime? FilterFrom { get; set; }
        public DateTime? FilterTo { get; set; }

        // ─── CHARTS ───
        public List<ServiceRevenueItem> TopServices { get; set; } = new();
        public List<ArtistRevenueItem> TopArtists { get; set; } = new();
        public List<ProvinceBookingItem> BookingsByProvince { get; set; } = new();
        public List<MonthlyRevenueItem> MonthlyTrend { get; set; } = new();

        // ─── REPORT TABLE ───
        public List<BookingReportItem> FilteredBookings { get; set; } = new();
    }

    // ─── TOP SERVICES ───
    public class ServiceRevenueItem
    {
        public string ServiceName { get; set; } = "";
        public int BookingCount { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal TotalPlatformFees { get; set; }    // ✅ NEW: Platform fees from this service
    }

    // ─── TOP ARTISTS ───
    public class ArtistRevenueItem
    {
        public string ArtistName { get; set; } = "";
        public string Province { get; set; } = "";
        public int BookingCount { get; set; }
        public decimal TotalRevenue { get; set; }              // Artist payout
        public decimal TotalPlatformEarnings { get; set; }     // ✅ NEW: Platform fees from this artist
        public int NewClients { get; set; }                    // ✅ NEW: New client count
        public int RepeatClients { get; set; }                 // ✅ NEW: Repeat client count
    }

    // ─── BOOKINGS BY PROVINCE ───
    public class ProvinceBookingItem
    {
        public string Province { get; set; } = "";
        public int BookingCount { get; set; }
        public decimal TotalRevenue { get; set; }              // Artist payout
        public decimal TotalPlatformEarnings { get; set; }     // ✅ NEW: Platform fees from this province
    }

    // ─── MONTHLY REVENUE TREND ───
    public class MonthlyRevenueItem
    {
        public string Month { get; set; } = "";
        public int BookingCount { get; set; }
        public decimal TotalRevenue { get; set; }              // Artist payout
        public decimal TotalPlatformEarnings { get; set; }     // ✅ NEW: Platform fees this month
        public decimal TotalClientPaid { get; set; }           // ✅ NEW: Client payments this month
    }

    // ─── ✅ FIXED: INDIVIDUAL BOOKING ROW ───
    public class BookingReportItem
    {
        public int BookingId { get; set; }
        public DateTime AppointmentDate { get; set; }
        public string ClientName { get; set; } = "";
        public string ArtistName { get; set; } = "";
        public string ServiceName { get; set; } = "";
        public string Province { get; set; } = "";
        public string Status { get; set; } = "";

        // ─── CLIENT TYPE ───
        public string ClientType { get; set; } = "New";        // ✅ NEW: "New" or "Repeat"

        // ─── ✅ NEW PRICING BREAKDOWN ───
        public decimal ServicePrice { get; set; }              // Artist's price (100%)
        public decimal ClientMarkup { get; set; }              // 4% markup (platform earns)
        public decimal PlatformFee { get; set; }               // 10% or R15 (platform earns)
        public decimal BookingFee { get; set; }                // R5 (platform earns)
        public decimal ClientTotal { get; set; }               // What client pays
        public decimal ArtistNet { get; set; }                 // What artist gets
        public decimal PlatformEarnings { get; set; }          // Total platform revenue

        // ─── BACKWARD COMPATIBILITY ───
        public decimal Amount { get; set; }                    // Alias for ArtistNet
    }
}