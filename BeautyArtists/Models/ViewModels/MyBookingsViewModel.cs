using System.Collections.Generic;

namespace BeautyArtists.Models.ViewModels
{
    public class MyBookingsViewModel
    {
        public List<BookingWithReviewStatus> Bookings { get; set; }
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