namespace BeautyArtists.Models.ViewModels
{
    public class ArtistPortfolioViewModel
    {
        public List<PortfolioItemViewModel> PortfolioItems { get; set; } = new();
        public List<string> AvailableCategories { get; set; } = new();
    }
   
}
