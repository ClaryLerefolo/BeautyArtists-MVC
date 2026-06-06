using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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

        public string Province { get; set; }
        public string City { get; set; }

        public string? ImagePath { get; set; }  // Example: "uploads/services/image1.jpg"

  

        [Required]
        [Range(1, 480)]
        public int Duration { get; set; } // Duration in minutes

        [Required]
        [Range(0, 10000)]
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Base Price (ZAR)")]
  
        public decimal BasePrice { get; set; } // Admin-suggested base price
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsFeatured { get; set; } = false;
        public ICollection<ArtistProfile> ArtistProfiles { get; set; }
        public ICollection<Review> Reviews { get; set; }
        // FOREIGN KEY FOR CATEGORY
        [Required]
        public int CategoryId { get; set; }

        [ForeignKey("CategoryId")]
        public ServiceCategory ServiceCategory { get; set; } // This name must match the .Include()

      

    }
}
