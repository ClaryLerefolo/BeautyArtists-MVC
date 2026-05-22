using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BeautyArtists.Models
{
    public class UserService
    {
        public int Id { get; set; }

        [Required]
        public string ArtistId { get; set; }

        [ForeignKey("ArtistId")]
        public ApplicationUser Artist { get; set; }

        [Required]
        public int ServiceId { get; set; }

        [ForeignKey("ServiceId")]
        public Service Service { get; set; }

        [Required]
        public decimal Price { get; set; }

        [Required]
        public int Duration { get; set; }

        public string? CustomDescription { get; set; }

        // ADD THIS LINE RIGHT HERE
        public string? ImagePath { get; set; }

        public bool IsActive { get; set; } = true;
    }
}

