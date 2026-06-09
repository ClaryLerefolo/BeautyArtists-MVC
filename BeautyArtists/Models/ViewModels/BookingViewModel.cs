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
        public string? ArtistName { get; set; }
        public string? ArtistId { get; set; }      // NEW: needed to fetch slots
        public string? ArtistProfilePicture { get; set; } // NEW: for the UI header
        public string? CategoryName { get; set; }  // NEW: for context

        // NEW: The selected availability slot ID
        [Required(ErrorMessage = "Please select an available time slot.")]
        public int AvailabilitySlotId { get; set; }

        // Kept for reschedule compatibility — auto-filled from slot
        public DateTime PreferredDate { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }

        [Required(ErrorMessage = "Please select whether you want a Walk-In or House Call.")]
        public LocationType SelectedLocationType { get; set; }

        public string? HouseCallAddress { get; set; }
        public string? Latitude { get; set; }
        public string? Longitude { get; set; }
    }
}
