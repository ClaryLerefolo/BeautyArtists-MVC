using BeautyArtists.Controllers;

namespace BeautyArtists.Models.ViewModels
{
    public class ServiceListViewModel
    {
        // ─── CONSTANTS FOR FEE STRUCTURE ───
        private const decimal CLIENT_MARKUP = 0.04m;      // 4% added to client
        private const decimal BOOKING_FEE = 5.00m;        // R5 booking fee

        public string Title { get; set; } = "Services";
        public string? ArtistId { get; set; }
        public string? ArtistName { get; set; }
        public string? ArtistLocation { get; set; }
        public string? ArtistProfilePicture { get; set; }
        public ServiceCategory? Category { get; set; }
        public string? Province { get; set; }
        public string? City { get; set; }
        public List<ServiceItem> Services { get; set; } = new();
        public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; } = 1;
        public int TotalCount { get; set; } = 0;

        // ─── HELPER: Calculate client total with 4% markup + R5 ───
        public static decimal GetClientTotal(decimal servicePrice)
        {
            var markedUpPrice = servicePrice + (servicePrice * CLIENT_MARKUP);
            return markedUpPrice + BOOKING_FEE;
        }

        // ─── HELPER: Get client price display ───
        public static string GetClientPriceDisplay(decimal servicePrice)
        {
            return $"R {GetClientTotal(servicePrice):N2}";
        }

        // ─── HELPER: Get full price breakdown ───
        public static PriceBreakdown GetPriceBreakdown(decimal servicePrice)
        {
            var markup = servicePrice * CLIENT_MARKUP;
            var clientTotal = servicePrice + markup + BOOKING_FEE;

            return new PriceBreakdown
            {
                ArtistPrice = servicePrice,
                PlatformMarkup = markup,
                BookingFee = BOOKING_FEE,
                ClientTotal = clientTotal,
                MarkupPercentage = CLIENT_MARKUP * 100
            };
        }

        public class ServiceItem
        {
            public int UserServiceId { get; set; }
            public string ServiceName { get; set; } = "";
            public string? Description { get; set; }
            public string Category { get; set; } = "";
            public int CategoryId { get; set; }

            public decimal Price { get; set; } // Artist's price (what artist gets)
            public string? ImagePath { get; set; }
            public string? ArtistName { get; set; }
            public string? ArtistId { get; set; }
            public string? Province { get; set; }
            public string? City { get; set; }
            public double AverageRating { get; set; }
            public int ReviewCount { get; set; }
            public string? ArtistLocation { get; set; }

            // ─── ✅ FIXED: Client-facing price with 4% markup + R5 ───
            public decimal ClientPrice => Price + (Price * 0.04m) + 5.00m;

            // ─── ✅ FIXED: Formatted client price display ───
            public string ClientPriceDisplay => $"R {ClientPrice:N2}";

            // ─── ✅ NEW: Full price breakdown ───
            public PriceBreakdown PriceBreakdown => new PriceBreakdown
            {
                ArtistPrice = Price,
                PlatformMarkup = Price * 0.04m,
                BookingFee = 5.00m,
                ClientTotal = ClientPrice,
                MarkupPercentage = 4
            };
        }
    }

    // ─── ✅ NEW: Price Breakdown Class ───
    public class PriceBreakdown
    {
        public decimal ArtistPrice { get; set; }
        public decimal PlatformMarkup { get; set; }
        public decimal BookingFee { get; set; }
        public decimal ClientTotal { get; set; }
        public decimal MarkupPercentage { get; set; }

        // ─── Helper for display ───
        public string ArtistPriceDisplay => $"R {ArtistPrice:N2}";
        public string PlatformMarkupDisplay => $"R {PlatformMarkup:N2}";
        public string BookingFeeDisplay => $"R {BookingFee:N2}";
        public string ClientTotalDisplay => $"R {ClientTotal:N2}";
    }
}