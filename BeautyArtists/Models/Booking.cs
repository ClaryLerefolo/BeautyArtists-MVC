using System;
using System.ComponentModel.DataAnnotations;

namespace BeautyArtists.Models
{
    public class Booking
    {
        public int Id { get; set; }

        [Required]
        public string CustomerId { get; set; }

        [Required]
        public int UserServiceId { get; set; } // Foreign Key to UserService

        public DateTime BookingDate { get; set; }


        public DateTime AppointmentDate { get; set; }


        [DataType(DataType.Currency)]

        public decimal TotalAmount { get; set; }

        public string? Notes { get; set; }
        public string? ArtistNotes { get; set; }   // Artist's comment when confirming/rejecting
        public string? ClientNotes { get; set; }    // Client's reason when cancelling/rescheduling
        public bool HasRescheduled { get; set; } = false;

        // Navigation property
        public virtual ApplicationUser Customer { get; set; }
        public virtual UserService UserService { get; set; }

        public int? AvailabilitySlotId { get; set; }
        public virtual ArtistAvailability AvailabilitySlot { get; set; }

        // Enum to represent different booking statuses
        public BookingStatus Status { get; set; }

        // Enum to represent the status of a booking
        public enum BookingStatus
        {
            Pending,      // Booking is created but not yet confirmed
            Confirmed,    // Booking is confirmed
            Completed,    // Booking has been completed
            Cancelled,    // Booking has been cancelled
            Rejected
        }
    }
}
