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
        // 🔥 NEW: Booking Fee & Client Total
        // ============================================================
        public decimal BookingFee { get; set; } = 6.00m;  // R6 fixed booking fee
        public decimal ClientTotal { get; set; }           // Price + BookingFee (what client pays)

        public string? ArtistName { get; set; }
        public string? ArtistId { get; set; }      // Needed to fetch slots
        public string? ArtistProfilePicture { get; set; } // For the UI header
        public string? CategoryName { get; set; }  // For context

        [Required(ErrorMessage = "Please select an available time slot.")]
        public int AvailabilitySlotId { get; set; }

        // Kept for reschedule compatibility — auto-filled from slot
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