using System;
using System.ComponentModel.DataAnnotations;

namespace BeautyArtists.Models.ViewModels
{
    public class ReviewViewModel
    {
        public int BookingId { get; set; }

        [Required]
        [Range(1, 5, ErrorMessage = "Please select a rating between 1 and 5.")]
        public int Rating { get; set; }

        [StringLength(500, ErrorMessage = "Comment cannot exceed 500 characters.")]
        public string? Comment { get; set; }

        // Display only
        public string? ServiceName { get; set; }
        public string? ArtistName { get; set; }
        public DateTime AppointmentDate { get; set; }
    }
}