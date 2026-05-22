using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BeautyArtists.Models
{
    public class ArtistAvailability
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string ArtistId { get; set; }

        [ForeignKey("ArtistId")]
        public virtual ApplicationUser Artist { get; set; }

        [Required]
        [Display(Name = "Date")]
        [DataType(DataType.Date)]
        public DateTime AvailableDate { get; set; }

        [Required]
        [Display(Name = "Start Time")]
        [DataType(DataType.Time)]
        public TimeSpan StartTime { get; set; }

        [Required]
        [Display(Name = "End Time")]
        [DataType(DataType.Time)]
        public TimeSpan EndTime { get; set; }

        public bool IsBooked { get; set; } = false;

        // Helper to check if the date has passed
        public bool IsExpired => AvailableDate.Date < DateTime.Now.Date;
    }
}