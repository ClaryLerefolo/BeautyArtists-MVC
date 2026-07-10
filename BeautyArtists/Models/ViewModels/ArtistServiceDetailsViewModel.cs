namespace BeautyArtists.Models.ViewModels
{
    public class ArtistServiceDetailViewModel
    {
        public UserService UserService { get; set; }
        public List<PortfolioItem> PortfolioItems { get; set; }
        public int DefaultPortfolioId { get; set; }
    }
}
