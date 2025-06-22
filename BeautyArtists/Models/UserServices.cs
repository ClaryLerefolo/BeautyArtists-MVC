using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BeautyArtists.Models
{
    public class UserService
    {
        public int Id { get; set; }

        [Required]
        public string ArtistId { get; set; }

        [ForeignKey("ArtistId")]
        public ApplicationUser Artist { get; set; }

        [Required]
        public int ServiceId { get; set; }

        [ForeignKey("ServiceId")]
        public Service Service { get; set; }

        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "Enter a valid price")]
        public decimal Price { get; set; }
        [Required]
        public int Duration { get; set; }

        public string CustomDescription { get; set; }
        public int? PortfolioCategoryId { get; set; }
        public PortfolioCategory? PortfolioCategory { get; set; }


        // Add this property:
        public bool IsActive { get; set; } = true;

    }
}

