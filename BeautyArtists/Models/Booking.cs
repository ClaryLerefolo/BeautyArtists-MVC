using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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
        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; set; } // Base Service Price + TransportCost + BookingFee (Client Total)

        [DataType(DataType.Currency)]
        [Column(TypeName = "decimal(18,2)")]
        public decimal TransportCost { get; set; } = 0; // Set by Artist later for house calls.

        // ============================================================
        // 🔥 NEW: Booking Fee & Commission Fields
        // ============================================================

        [DataType(DataType.Currency)]
        [Column(TypeName = "decimal(18,2)")]
        public decimal BookingFee { get; set; } = 6.00m; // R6 fixed client booking fee

        [DataType(DataType.Currency)]
        [Column(TypeName = "decimal(18,2)")]
        public decimal ServicePrice { get; set; } = 0m; // Original artist price (before fees)

        [DataType(DataType.Currency)]
        [Column(TypeName = "decimal(18,2)")]
        public decimal PlatformCommission { get; set; } = 0m; // 15% commission deducted from artist

        [DataType(DataType.Currency)]
        [Column(TypeName = "decimal(18,2)")]
        public decimal PlatformEarnings { get; set; } = 0m; // BookingFee + Commission (total platform revenue)

        [DataType(DataType.Currency)]
        [Column(TypeName = "decimal(18,2)")]
        public decimal ArtistNetAmount { get; set; } = 0m; // What the artist actually receives (85% of service price)

        [DataType(DataType.Currency)]
        [Column(TypeName = "decimal(18,2)")]
        public decimal ArtistTotalEarned { get; set; } = 0m; // Accumulated earnings for this booking (deposit + final)

        // ============================================================
        // Existing properties below...
        // ============================================================

        [Required(ErrorMessage = "Please select whether you want a Walk-In or House Call.")]
        public LocationType SelectedLocationType { get; set; } // WalkIn or HouseCall

        public string? HouseCallAddress { get; set; } //Full combined (for backward compatibility)
        public string? HouseNumber { get; set; }
        public string? StreetAddress { get; set; }
        public string? AreaCode { get; set; }
        public string? Latitude { get; set; }         // Lat coordinate for the map pin
        public string? Longitude { get; set; }        // Lng coordinate for the pin.
        public string? Notes { get; set; }
        public string? ArtistNotes { get; set; }      // Artist's comment when confirming/rejecting
        public string? ClientNotes { get; set; }      // Client's reason when cancelling/rescheduling
        public bool HasRescheduled { get; set; } = false;
        public bool IsDepositPaid { get; set; } = false;

        // Navigation properties
        public virtual ApplicationUser Customer { get; set; } = default!;
        public virtual UserService UserService { get; set; } = default!;

        public int? AvailabilitySlotId { get; set; }
        public virtual ArtistAvailability? AvailabilitySlot { get; set; } // Fixed to match its nullable foreign key
        public decimal DepositPaid { get; set; } = 0m;
        public decimal FinalPaymentPaid { get; set; } = 0m;
        public decimal TotalPaid => DepositPaid + FinalPaymentPaid;
        public bool IsFullyPaid => TotalPaid >= TotalAmount;
        public DateTime? DepositPaidDate { get; set; }
        public DateTime? FinalPaidDate { get; set; }
        public bool IsLocationShared { get; set; } = false;


        // Enum to represent different booking statuses
        public BookingStatus Status { get; set; }

        public enum BookingStatus
        {
            Pending,      // Booking is created but not yet confirmed
            Accepted,     // Artist accepted, waiting for deposit payment
            Confirmed,    // Booking is confirmed
            Completed,    // Booking has been completed
            Cancelled,    // Booking has been cancelled
            Rejected
        }
    }

    public enum LocationType
    {
        [Display(Name = "Walk-In (At Salon/Studio)")]
        WalkIn,
        [Display(Name = "House Call (Artist Travels to You)")]
        HouseCall
    }
}