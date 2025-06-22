namespace BeautyArtists.Models.ViewModels
{
    public class ArtistServiceViewModel
    {
        public string ArtistId { get; set; }
        public string FullName { get; set; }
        public string Location { get; set; }
        public string ProfilePictureUrl { get; set; }
        public List<ServiceItem> Services { get; set; } = new List<ServiceItem>();

        public class ServiceItem
        {
            public int UserServiceId { get; set; }
            public string ServiceName { get; set; }
            public decimal Price { get; set; }
            public string Description { get; set; }
            public string Category { get; set; }
        }
    }
}
