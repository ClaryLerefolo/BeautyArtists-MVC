using System.ComponentModel.DataAnnotations;


namespace BeautyArtists.Models.ViewModels
{
    public class PortfolioCreateViewModel
    {
        [Required]
        [StringLength(100, MinimumLength = 3)]

        public string Name { get; set; }  // e.g., "Bridal Collection"
        public List<IFormFile>? Images { get; set; } // for multiple uploads


        [Required]
        [StringLength(1000)]

        public string? Description { get; set; }

    }
}
