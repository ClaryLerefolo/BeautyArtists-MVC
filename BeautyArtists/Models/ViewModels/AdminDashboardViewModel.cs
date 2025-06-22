namespace BeautyArtists.Models.ViewModels
{
    public class AdminDashboardViewModel
    {

      
        public int TotalUsers { get; set; }
        public int TotalArtists { get; set; }

        public int TotalCustomers { get; set; }

        public int TotalBookings { get; set; }

        public decimal TotalRevenue { get; set; }



        public List<ArtistRevenue> RevenuePerArtist { get; set; }


        public class ArtistRevenue
        {
            public string ArtistId { get; set; }

            public decimal TotalRevenue { get; set; }
        }
        
          

    }
}
