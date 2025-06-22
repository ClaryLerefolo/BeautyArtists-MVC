namespace BeautyArtists.Models
{
    public class PortfolioImage
    {
        public int Id { get; set; }
        public int PortfolioId { get; set; }
        public string ImagePath { get; set; } = string.Empty;

        public Portfolio Portfolio { get; set; }
    }

}
