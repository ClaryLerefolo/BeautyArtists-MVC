using System.ComponentModel.DataAnnotations;

namespace BeautyArtists.Models
{
    public class Review
    {
        public int Id { get; set; }

        [Required]
        [Range(1, 5)]
        public int Rating { get; set; }

        public string Comment { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Relationships
        public string CustomerId { get; set; }
        public ApplicationUser Customer { get; set; }

        public int ServiceId { get; set; }
        public Service Service { get; set; }

        // Link to booking
        public int BookingId { get; set; }
        public Booking Booking { get; set; }
    }

}
