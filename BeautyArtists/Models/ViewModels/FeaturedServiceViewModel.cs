namespace BeautyArtists.Models.ViewModels
{
    public class FeaturedServiceViewModel
    {
        public int UserServiceId { get; set; }

        public string ServiceName { get; set; }
        public string Category { get; set; }
        public decimal Price { get; set; }
        public string ImagePath { get; set; }

        public string ArtistName { get; set; }

        public double AverageRating { get; set; }
        public int ReviewCount { get; set; }

        public string HighlightComment { get; set; }
    }

}
