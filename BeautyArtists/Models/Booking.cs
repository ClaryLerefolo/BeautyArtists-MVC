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
        //  Card Processing Fee & Client Type
        // ============================================================
        [DataType(DataType.Currency)]
        [Column(TypeName = "decimal(18,2)")]
        public decimal CardProcessingFee { get; set; } = 0m; // 4% × P (charged to client)

        public bool IsNewClient { get; set; } = false; // True = new client, False = repeat client

        // ============================================================
        // 🔥 FIXED: Booking Fee & Commission Fields
        // ============================================================

        [DataType(DataType.Currency)]
        [Column(TypeName = "decimal(18,2)")]
        public decimal BookingFee { get; set; } = 5.00m; // R5 fixed client booking fee (NOT R6!)

        [DataType(DataType.Currency)]
        [Column(TypeName = "decimal(18,2)")]
        public decimal ServicePrice { get; set; } = 0m; // Original artist price (before fees)

        [DataType(DataType.Currency)]
        [Column(TypeName = "decimal(18,2)")]
        public decimal PlatformCommission { get; set; } = 0m; // Commission deducted from artist

        [DataType(DataType.Currency)]
        [Column(TypeName = "decimal(18,2)")]
        public decimal PlatformEarnings { get; set; } = 0m; // CardFee + BookingFee + Commission (total platform revenue)

        [DataType(DataType.Currency)]
        [Column(TypeName = "decimal(18,2)")]
        public decimal ArtistNetAmount { get; set; } = 0m; // What the artist actually receives

        [DataType(DataType.Currency)]
        [Column(TypeName = "decimal(18,2)")]
        public decimal ArtistTotalEarned { get; set; } = 0m; // Accumulated earnings for this booking

        // ============================================================
        // 🔥 NEW: Deposit & Final Amounts (stored at booking time)
        // ============================================================
        [DataType(DataType.Currency)]
        [Column(TypeName = "decimal(18,2)")]
        public decimal DepositAmount { get; set; } = 0m; // 50% of service + card fee + booking fee = R221.00

        [DataType(DataType.Currency)]
        [Column(TypeName = "decimal(18,2)")]
        public decimal FinalAmount { get; set; } = 0m; // 50% of service = R200.00

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
        // ─── CANCELLATION/REFUND TRACKING ───
        [DataType(DataType.Currency)]
        [Column(TypeName = "decimal(18,2)")]
        public decimal RefundAmount { get; set; } = 0m;

        public DateTime? RefundDate { get; set; }

        public bool IsRefunded { get; set; } = false;

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
        // ─── BOOKING LIFECYCLE PROPERTIES ───
        public DateTime? ConfirmationPromptSentAt { get; set; }  // When client was asked to confirm
        public DateTime? AutoConfirmAt { get; set; }             // 5 hours after appointment
        public bool IsDisputed { get; set; } = false;
        public string? DisputeReason { get; set; }               // "no_show" or "quality_issue"
        public DateTime? DisputeRaisedAt { get; set; }
        public DateTime? AdminReviewedAt { get; set; }
        public string? AdminResolution { get; set; }             // "release_to_artist", "refund_to_client", "partial_split"
        public decimal AdminResolutionAmount { get; set; } = 0m;
        public DateTime? CompletedAt { get; set; }
        public DateTime? FundsReleasedAt { get; set; }
        public bool IsFundsReleased { get; set; } = false;
        public bool IsCompleted { get; set; } = false;
        public string? DisputeDescription { get; set; }
        public string? AdminNotes { get; set; }


        public enum BookingStatus
        {
            Pending,      // Booking is created but not yet confirmed
            Accepted,     // Artist accepted, waiting for deposit payment
            Confirmed,    // Booking is confirmed
            Completed,    // Booking has been completed
            Cancelled,    // Booking has been cancelled
            Disputed,     // Booking has beem dispiuted
            InReview,     // Dispute is under review by admin.
            Resolved,     // Dispute is resolved by admin.  
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