using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BeautyArtists.Models
{
    public class Payment
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int BookingId { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; }

        // ✅ NULLABLE
        public string? PhoneNumber { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        // ✅ NULLABLE
        public string? Reference { get; set; }

        public string Currency { get; set; } = "ZAR";

        // ✅ NULLABLE
        public string? Status { get; set; } = "pending";

        // ✅ NULLABLE
        public string? PaymentMethod { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? PaidAt { get; set; }

        public bool IsDeposit { get; set; } = true;

        [ForeignKey("BookingId")]
        public virtual Booking? Booking { get; set; }
        public bool IsFullPayment { get; set; }  // 🔥 NEW - For last-minute full payments

    }
}