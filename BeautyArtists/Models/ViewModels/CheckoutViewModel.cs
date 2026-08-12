namespace BeautyArtists.Models.ViewModels
{
    public class CheckoutViewModel
    {
        public Booking Booking { get; set; }
        public decimal DepositAmount { get; set; }
        public string UserEmail { get; set; }
        public string UserName { get; set; }
        public bool IsLastMinute { get; set; }

        // ─── ✅ NEW: Pricing breakdown for the new structure ───
        public bool IsNewClient { get; set; }        // True = new client (10% commission), False = repeat client (R15 flat fee)
        public decimal ArtistPayout { get; set; }    // What the artist actually receives after platform fee
        public decimal PlatformFee { get; set; }     // Platform fee deducted from artist (10% or R15)
        public decimal ClientMarkup { get; set; }    // 4% markup added to client
        public decimal BookingFee { get; set; }      // R5 booking fee
    }
}