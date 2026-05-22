namespace BeautyArtists.Models.ViewModels
{
    public class ArtistPortfolioModalViewModel
    {
        public string ArtistId { get; set; }
        public string ArtistName { get; set; }
        public string ServiceName { get; set; }
        public List<Portfolio> Portfolios { get; set; } = new();
    }
}
