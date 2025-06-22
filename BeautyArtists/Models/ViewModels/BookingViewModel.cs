using System;
using System.ComponentModel.DataAnnotations;

namespace BeautyArtists.Models.ViewModels
{
    public class BookingViewModel
    {
        public int BookingId { get; set; } // Needed for reschedule

        public int UserServiceId { get; set; }

        public string ServiceName { get; set; }

        public decimal Price { get; set; }

        public string ArtistName { get; set; }

        [Required(ErrorMessage = "Please select a preferred date and time.")]
        [DataType(DataType.DateTime)]
        [DisplayFormat(DataFormatString = "{0:yyyy-MM-ddTHH:mm}", ApplyFormatInEditMode = true)]

        [Display(Name = "Preferred Date and Time")]
        public DateTime PreferredDate { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }
    }
}
