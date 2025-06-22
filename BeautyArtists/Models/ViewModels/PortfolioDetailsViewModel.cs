namespace BeautyArtists.Models.ViewModels
{
    public class PortfolioDetailsViewModel
    {

        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<PortfolioItem> Items { get; set; } = new List<PortfolioItem>();
    }
}
