using System;
using System.Collections.Generic;

namespace BeautyArtists.Models.ViewModels
{
    public class ArtistEarningsViewModel
    {
        // ============================================================
        // ARTIST EARNINGS (Existing)
        // ============================================================
        public decimal TotalLifetimeEarnings { get; set; }
        public decimal ThisMonthEarnings { get; set; }
        public decimal PendingEarnings { get; set; }
        public int CompletedBookingsCount { get; set; }
        public List<EarningsHistoryItem> History { get; set; } = new();
        public decimal AvgJobValue { get; set; }
        public double RepeatClientRate { get; set; } // 0.0-1.0 → displays as 35%
        public double UtilizationRate { get; set; } // 0.0-1.0
        public List<KeyValuePair<string, EarningsServiceSummary>> TopServices { get; set; } = new();
        public decimal TotalDeposits { get; set; }
        public decimal TotalFinalPayments { get; set; }

        // ============================================================
        // 🔥 NEW: PLATFORM EARNINGS
        // ============================================================
        public decimal TotalPlatformLifetimeEarnings { get; set; }  // Total platform revenue (commission + booking fees)
        public decimal TotalBookingFeesCollected { get; set; }      // Total R6 booking fees collected
        public decimal TotalCommissionCollected { get; set; }       // Total 15% commission collected
        public decimal ThisMonthPlatformEarnings { get; set; }      // Platform earnings this month

        // ============================================================
        // 🔥 NEW: BREAKDOWN PER BOOKING
        // ============================================================
        public decimal TotalBookingFees { get; set; }               // Sum of all booking fees
        public decimal TotalArtistGross { get; set; }               // Sum of all service prices before commission
        public decimal TotalClientPaid { get; set; }                // Sum of all client totals (service + fee)
    }

    public class EarningsServiceSummary
    {
        public decimal TotalEarnings { get; set; }
        public int JobCount { get; set; }
    }

    public class EarningsHistoryItem
    {
        public int BookingId { get; set; }
        public DateTime Date { get; set; }
        public string ClientName { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;

        // ============================================================
        // EXISTING FIELDS
        // ============================================================
        public decimal TotalPaid { get; set; }          // What client actually paid (deposit + final)
        public decimal YourEarnings { get; set; }       // Artist's 85% cut
        public string Status { get; set; } = string.Empty;
        public decimal OriginalPrice { get; set; }      // Artist's service price
        public decimal PlatformFee { get; set; }        // 15% commission
        public decimal TipAmount { get; set; }
        public decimal DepositPaid { get; set; }
        public decimal FinalPaymentPaid { get; set; }
        public bool IsFullyPaid { get; set; }

        // ============================================================
        // 🔥 NEW: BOOKING FEE FIELDS
        // ============================================================
        public decimal BookingFee { get; set; }          // R6 booking fee
        public decimal ClientTotalPaid { get; set; }     // ServicePrice + BookingFee (what client was charged)
        public decimal PlatformTotalEarnings { get; set; } // BookingFee + Commission (platform revenue)
    }
}