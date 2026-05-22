using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace BeautyArtists.Models.ViewModels
{
    public class PortfolioItemViewModel
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Title { get; set; } = string.Empty;
        [Required]
        [StringLength(500)]
        public string? Description { get; set; }
        [Display(Name = "Category")]
      
        public int CategoryId { get; set; }

        public IEnumerable<SelectListItem>? Categories { get; set; }


        [Required]
        public string Province { get; set; }
        [Required]
        public string City { get; set; }

        [StringLength(100)]
        public string? ClientName { get; set; }

        [StringLength(100)]
        public string? MusicTrack { get; set; }

        [Required]
        [Range(1, 9999)]
        [Remote(action: "IsDisplayOrderUnique", controller: "PortfolioItem", AdditionalFields = "Id", ErrorMessage = "Display order must be unique.")]
        public int DisplayOrder { get; set; }

        [Required]
        [Display(Name = "Media Type")]
        public string MediaType { get; set; } = string.Empty;

        [Display(Name = "Upload Media")]
        [MaxFileSize(10 * 1024 * 1024)] // 10MB
        [AllowedExtensions(new[] { ".jpg", ".jpeg", ".png", ".mp4", ".mov", ".avi" })]
        public IFormFile? MediaFile { get; set; }

        [Display(Name = "Upload Thumbnail")]
        [MaxFileSize(10 * 1024 * 1024)]
        [AllowedExtensions(new[] { ".jpg", ".jpeg", ".png" })]
        public IFormFile? ThumbnailFile { get; set; }

        public string? ExistingMediaUrl { get; set; }
        public string? ExistingThumbnailUrl { get; set; }

        [Display(Name = "Featured Item")]
        public bool IsFeatured { get; set; }

        [Required]
        [Display(Name = "Portfolio")]
        public int PortfolioId { get; set; }

        public IEnumerable<SelectListItem>? PortfoliosSelectList { get; set; } = new List<SelectListItem>();

        public bool HasMedia =>
            MediaFile != null || !string.IsNullOrWhiteSpace(ExistingMediaUrl);
    }

    public class MaxFileSizeAttribute : ValidationAttribute
    {
        private readonly int _maxBytes;

        public MaxFileSizeAttribute(int maxBytes)
        {
            _maxBytes = maxBytes;
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is IFormFile file)
            {
                if (file.Length > _maxBytes)
                {
                    return new ValidationResult($"Maximum allowed file size is {_maxBytes / (1024 * 1024)} MB.");
                }
            }

            return ValidationResult.Success;
        }
    }

    public class AllowedExtensionsAttribute : ValidationAttribute
    {
        private readonly string[] _extensions;

        public AllowedExtensionsAttribute(string[] extensions)
        {
            _extensions = extensions;
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is IFormFile file)
            {
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!_extensions.Contains(extension))
                {
                    return new ValidationResult($"Only the following file extensions are allowed: {string.Join(", ", _extensions)}");
                }
            }

            return ValidationResult.Success;
        }
    }
}
