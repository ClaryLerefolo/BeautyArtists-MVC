namespace BeautyArtists.Models
{
    public class Notification
    {
        public int Id { get; set; }
        public string ArtistId { get; set; }
        public string Message { get; set; }
        public bool IsRead { get; set; } = false;
        public DateTime CreatedAt { get; set; }
    }

}
