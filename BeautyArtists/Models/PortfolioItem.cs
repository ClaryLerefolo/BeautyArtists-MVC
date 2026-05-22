using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BeautyArtists.Models
{
    public class PortfolioItem
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Title { get; set; }

        [ForeignKey("CategoryId")]
        public int? CategoryId { get; set; }
        public virtual ServiceCategory? Category { get; set; }

        public bool IsFeatured { get; set; } = false;
        public int DisplayOrder { get; set; }
        [Url]
        public string? ThumbnailUrl { get; set; } // For video previews or image galleries
                                                  // Replaced Location with these two
        [Required]
        public string Province { get; set; }
        [Required]
        public string City { get; set; }

        [StringLength(100)]
        public string? ClientName { get; set; }
        public string ArtistId { get; set; }

        [ForeignKey("ArtistId")]
        public ApplicationUser Artist { get; set; }

        [Required]
        [Url]

        public string MediaUrl { get; set; }

        [Required]
        [RegularExpression("^(Image|Video)$", ErrorMessage = "Media type must be 'Image' or 'Video'.")]

        public string MediaType { get; set; } // e.g., "Image" or "Video"

        public string? MusicTrack { get; set; }

        [StringLength(1000)]
        public string? Description { get; set; }

        public DateTime UploadedAt { get; set; }

        // 🔥 New: optional Portfolio reference
        public int? PortfolioId { get; set; }

        [ForeignKey("PortfolioId")]

        public Portfolio? Portfolio { get; set; }
    }
}
