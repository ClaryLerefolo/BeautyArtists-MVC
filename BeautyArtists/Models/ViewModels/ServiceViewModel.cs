using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BeautyArtists.Models.ViewModels
{
    public class ServiceViewModel
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; }

        public string? ImagePath { get; set; }

        public string? Description { get; set; }

        [Required]
        public decimal BasePrice { get; set; }

        [Required]
        [Display(Name = "Category")]
        public int CategoryId { get; set; }

        public bool IsFeatured { get; set; }

        public List<SelectListItem>? Categories { get; set; }
    }
}
