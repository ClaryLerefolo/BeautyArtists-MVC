namespace BeautyArtists.Models
{
    public class ArtistService
    {
        public int Id { get; set; }
        public string ArtistId { get; set; }
        public int ServiceId { get; set; }
        public decimal Price { get; set; }
        public bool IsActive { get; set; }

        // Navigation property
        public Service Service { get; set; }
    }
}
