namespace BeautyArtists.Models.ViewModels
{
    public class ServiceListViewModel
    {
        public string Title { get; set; } = "Services";
        public string? ArtistId { get; set; }
        public string? ArtistName { get; set; }
        public string? ArtistLocation { get; set; }
        public string? ArtistProfilePicture { get; set; }
        public ServiceCategory? Category { get; set; }
        public string? Province { get; set; }
        public string? City { get; set; }
        public List<ServiceItem> Services { get; set; } = new();

        public class ServiceItem
        {
            public int UserServiceId { get; set; }
            public string ServiceName { get; set; }
            public string? Description { get; set; }
            public string Category { get; set; }
            public int CategoryId { get; set; } // 🔥 ADD THIS

            public decimal Price { get; set; }
            public string? ImagePath { get; set; }
            public string? ArtistName { get; set; }

            public string? ArtistId { get; set; }
            public string? Province {get; set;} 
            public string? City { get; set; }
            public double AverageRating { get; set; }
            public int ReviewCount { get; set; }
            public string? ArtistLocation { get; set; }
        }
    }
}
