using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BeautyArtists.Models.ViewModels
{
    public class UserServiceViewModel
    {
        [Required(ErrorMessage = "Please select a service.")]
        [Display(Name = "Service")]
        public int ServiceId { get; set; }  // To select admin-defined service
        public int? PortfolioCategoryId { get; set; }

        public List<SelectListItem> AvailableServices { get; set; } = new List<SelectListItem>();

        [Required(ErrorMessage = "Please enter your price.")]
        [Range(0.01, 10000, ErrorMessage = "Enter a valid price.")]
        [Display(Name = "Your Price (ZAR)")]
        public decimal Price { get; set; }

        [StringLength(250, ErrorMessage = "Custom description max 250 characters.")]
        [Display(Name = "Custom Description (optional)")]
        public string? CustomDescription { get; set; }

        public bool IsActive { get; set; } = true;

        public int? Id { get; set; }  // Optional: for editing existing UserService
    }
}
