using System;
using System.Collections.Generic;

namespace BeautyArtists.Models.ViewModels
{
    public class ArtistEarningsViewModel
    {
        public decimal TotalLifetimeEarnings { get; set; }
        public decimal ThisMonthEarnings { get; set; }
        public decimal PendingEarnings { get; set; }
        public int CompletedBookingsCount { get; set; }
        public List<EarningsHistoryItem> History { get; set; } = new();
        public decimal AvgJobValue { get; set; }
        public double RepeatClientRate { get; set; } // 0.0-1.0 → displays as 35%
        public double UtilizationRate { get; set; } // 0.0-1.0
        public List<KeyValuePair<string, EarningsServiceSummary>> TopServices { get; set; } = new();

       
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
        public string ClientName { get; set; }
        public string ServiceName { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal YourEarnings { get; set; }
        public string Status { get; set; }

        public decimal OriginalPrice { get; set; }
        public decimal PlatformFee { get; set; }
        public decimal TipAmount { get; set; }

    }
}