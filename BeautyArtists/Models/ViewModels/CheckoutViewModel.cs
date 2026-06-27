namespace BeautyArtists.Models.ViewModels
{
    public class CheckoutViewModel
    {
        public Booking Booking { get; set; }
        public decimal DepositAmount { get; set; }
        public string UserEmail { get; set; }
        public string UserName { get; set; }

        public bool IsLastMinute { get; set; }

    }
}