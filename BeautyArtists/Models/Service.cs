using System.ComponentModel.DataAnnotations;

namespace BeautyArtists.Models
{
    public class Service
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; }

        [Required]
        [StringLength(100)]
        public string Description { get; set; }

        public string? ImagePath { get; set; }  // Example: "uploads/services/image1.jpg"

        // Replace old Category string
        public int CategoryId { get; set; }
        public PortfolioCategory? Category { get; set; }  // e.g., Hair, Makeup, Nails

        [Required]
        [Range(1, 480)]
        public int Duration { get; set; } // Duration in minutes

        [Required]
        [Range(0, 10000)]
        [Display(Name = "Base Price (ZAR)")]

        public decimal BasePrice { get; set; } // Admin-suggested base price
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsFeatured { get; set; } = false;
        public ICollection<ArtistProfile> ArtistProfiles { get; set; }



    }
}
