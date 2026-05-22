namespace BeautyArtists.Models.ViewModels
{
    public class ArtistDashboardViewModel
    {
        public string ArtistName { get; set; } = string.Empty;

        public int UpcomingAppointments { get; set; }

        public int PortfolioItemsCount { get; set; }

        public int ServicesCount { get; set; }

        public decimal MonthlyEarnings { get; set; }

        // NEW: Data for the Earnings Trend Graph
        public List<decimal> MonthlyEarningsGraph { get; set; } = new List<decimal>();
        public List<string> MonthlyLabels { get; set; } = new List<string>();

        public List<AppointmentSummary> RecentAppointments { get; set; } = new List<AppointmentSummary>();
        public string FullName { get; set; } // Add this!
    }

    public class AppointmentSummary
    {
        public string ClientName { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public DateTime AppointmentDate { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}

