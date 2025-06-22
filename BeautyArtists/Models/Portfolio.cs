using System.ComponentModel.DataAnnotations;

namespace BeautyArtists.Models
{
    public class Portfolio
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100, MinimumLength = 3)]

        public string Name { get; set; }  // e.g., "Bridal Collection"

        [Required]
        [StringLength(1000)]

        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public string ArtistId { get; set; }

        public ApplicationUser Artist { get; set; }

        public List<PortfolioItem> Items { get; set; } = new List<PortfolioItem>();
    }
}
