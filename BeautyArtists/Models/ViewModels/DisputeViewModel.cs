using System.ComponentModel.DataAnnotations;

namespace BeautyArtists.Models.ViewModels
{
    public class DisputeViewModel
    {
        public int BookingId { get; set; }
        public string? ServiceName { get; set; }

        [Required(ErrorMessage = "Please select a dispute reason.")]
        public string? Reason { get; set; } // "no_show" or "quality_issue"

        [Required(ErrorMessage = "Please provide details about the dispute.")]
        [StringLength(1000, ErrorMessage = "Details cannot exceed 1000 characters.")]
        public string? Description { get; set; }
    }
}