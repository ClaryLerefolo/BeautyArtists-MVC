using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BeautyArtists.Models
{
    public class Notification
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } // Who receives this notification

        [Required]
        public string Title { get; set; } // "Appointment Accepted", "Payment Received"

        [Required]
        public string Message { get; set; } // Detailed message

        public string Type { get; set; } // "booking", "payment", "reminder", "system"

        public string ReferenceId { get; set; } // BookingId, PaymentId, etc.

        public string ReferenceType { get; set; } // "Booking", "Payment", "User"

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsRead { get; set; } = false;

        public DateTime? ReadAt { get; set; }

        public string Icon { get; set; } // Emoji or icon class

        public string ActionUrl { get; set; } // Link to click when notification is tapped
    }
}