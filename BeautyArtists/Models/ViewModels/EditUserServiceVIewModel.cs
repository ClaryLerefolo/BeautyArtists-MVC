using System.ComponentModel.DataAnnotations;

namespace BeautyArtists.Models.ViewModels
{
    public class EditUserServiceViewModel
    {
        public int Id { get; set; }

        [Display(Name = "Your Price (ZAR)")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Enter a valid price")]
        public decimal Price { get; set; }
       
        public int Duration { get; set; }


        [Display(Name = "Custom Description")]
        public string? CustomDescription { get; set; }

        [Display(Name = "Active")]
        public bool IsActive { get; set; }

        // Read-only display properties
        public string ServiceName { get; set; }
        public decimal BasePrice { get; set; }
    }
}
