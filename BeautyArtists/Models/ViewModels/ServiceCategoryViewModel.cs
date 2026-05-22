using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace BeautyArtists.Models.ViewModels
{
    public class ServiceCategoryViewModel
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; }

        public string? IconName { get; set; }

        public string? CoverImagePath { get; set; } // Will store path

        public string? ExistingImagePath { get; set; }

        public IFormFile? ImageFile { get; set; } //  File upload
    }
}
