using BeautyArtists.Models;
using System.Collections.Generic;

namespace BeautyArtists.Models.ViewModels
{
    public class ArtistServicePortfolioViewModel
    {
        public string ArtistId { get; set; }
        public string ArtistName { get; set; }
        public string ArtistProfilePicture { get; set; }
        public string ServiceName { get; set; }
        public string ServiceDescription { get; set; }
        public int UserServiceId { get; set; }
        public decimal ServicePrice { get; set; }
        public List<Portfolio> Portfolios { get; set; }
    }
}