using System.ComponentModel.DataAnnotations;


namespace BeautyArtists.Models.ViewModels
{
    public class PortfolioItemCreateViewModel
    {
        public int PortfolioId { get; set; }

        [Required]
        public string Title { get; set; }

        [Required]
        public string MediaType { get; set; } // "Image" or "Video"

        [Required]
        public IFormFile MediaFile { get; set; } // File to upload

        public string? Description { get; set; }
    }

}
