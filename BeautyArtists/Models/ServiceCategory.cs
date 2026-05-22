using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BeautyArtists.Models
{
    [Table("ServiceCategories")] // <-- Explicitly set the new table name

    public class ServiceCategory
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; }
        // New: This will hold the Bootstrap icon name (e.g. "scissors", "droplet", etc.)
        [StringLength(100)]
        public string? IconName { get; set; }
        public string CoverImagePath { get; set; }
        public ICollection<Service>? Services { get; set; }
        public ICollection<PortfolioItem>? PortfolioItems { get; set; }

    }
}
