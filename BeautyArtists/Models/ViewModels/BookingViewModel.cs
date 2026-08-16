using System;
using System.ComponentModel.DataAnnotations;

namespace BeautyArtists.Models.ViewModels
{
    public class BookingViewModel
    {
        public int BookingId { get; set; }         // For reschedule
        public int UserServiceId { get; set; }
        public string? ServiceName { get; set; }
        public decimal Price { get; set; }

        // ============================================================
        // 🔥 NEW: Card Processing Fee & Client Total
        // ============================================================
        public decimal CardProcessingFee { get; set; }
        public decimal BookingFee { get; set; } = 5.00m;  // R5 flat booking fee
        public decimal ClientTotal { get; set; }         // Price + CardProcessingFee (what client pays)
        public bool IsNewClient { get; set; }            // New or repeat client

        // ============================================================
        // 🔥 NEW: "I Agree" acknowledgment
        // ============================================================
        public bool HasAgreedToTerms { get; set; }       // Client must agree before booking

        public string? ArtistName { get; set; }
        public string? ArtistId { get; set; }
        public string? ArtistProfilePicture { get; set; }
        public string? CategoryName { get; set; }

        [Required(ErrorMessage = "Please select an available time slot.")]
        public int AvailabilitySlotId { get; set; }

        public DateTime PreferredDate { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        [Required(ErrorMessage = "Please select whether you want a Walk-In or House Call.")]
        public LocationType? SelectedLocationType { get; set; }

        public string? HouseCallAddress { get; set; }
        public string? Latitude { get; set; }
        public string? Longitude { get; set; }
        public string? HouseNumber { get; set; }
        public string? StreetAddress { get; set; }
        public string? AreaCode { get; set; }

        public bool IsLocationShared { get; set; } = false;
        public string? StudioAddress { get; set; }
        public string? StudioCity { get; set; }
        public string? StudioProvince { get; set; }
        public double? StudioLatitude { get; set; }
        public double? StudioLongitude { get; set; }
    }
}