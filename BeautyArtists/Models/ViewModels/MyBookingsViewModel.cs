using System.Collections.Generic;

namespace BeautyArtists.Models.ViewModels
{
    public class MyBookingsViewModel
    {
        public List<BookingWithReviewStatus> Bookings { get; set; } = new();
        public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; } = 1;
        public int TotalCount { get; set; } = 0;
        public int PageSize { get; set; } = 10; // ✅ Added

        // Optional: Helper properties for the view
        public int StartIndex => (CurrentPage - 1) * PageSize + 1;
        public int EndIndex => Math.Min(CurrentPage * PageSize, TotalCount);
    }

    public class BookingWithReviewStatus
    {
        public Booking Booking { get; set; }
        public bool HasReviewed { get; set; }

        public string? StudioAddress { get; set; }
        public string? StudioCity { get; set; }
        public string? StudioProvince { get; set; }
        public double? StudioLatitude { get; set; }
        public double? StudioLongitude { get; set; }
    }
}