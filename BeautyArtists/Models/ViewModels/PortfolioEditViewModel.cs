using System.ComponentModel.DataAnnotations;

namespace BeautyArtists.Models.ViewModels
{
    public class PortfolioEditViewModel
    {
        public int Id { get; set; }
        [Required]
        [StringLength(100, MinimumLength = 3)]

        public string Name { get; set; }  // e.g., "Bridal Collection"
        public List<IFormFile>? NewImages { get; set; } // for new uploads
        public List<PortfolioImage>? ExistingImages { get; set; }

        [Required]
        [StringLength(1000)]

        public string? Description { get; set; }
    }
}
