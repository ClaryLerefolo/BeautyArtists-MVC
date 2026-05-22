namespace BeautyArtists.Models
{
    public class ActivityLog
    {
        public int Id { get; set; }
        public string ArtistId { get; set; }
        public string? Description { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Action { get; set; }
        public ApplicationUser Artist { get; set; }

    }

}
