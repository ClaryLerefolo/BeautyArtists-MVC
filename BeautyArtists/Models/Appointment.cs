using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BeautyArtists.Models
{
    public class Appointment
    {
        public int Id { get; set; }

        [Required]
        public string CustomerName { get; set; }

        [Required, EmailAddress]
        public string CustomerEmail { get; set; }

        [Required, Phone]
        public string CustomerPhone { get; set; }

        [Required]
        public DateTime AppointmentDate { get; set; }

        [Required]
        public TimeSpan AppointmentTime { get; set; }

        public string Notes { get; set; }

        // Foreign Keys
        [Required]
        public int ServiceId { get; set; }

        public Service Service { get; set; }

        [Required]
        public string ArtistId { get; set; }

        [ForeignKey("ArtistId")]
        public ApplicationUser Artist { get; set; }

        public bool IsConfirmed { get; set; } = false;
    }
}
