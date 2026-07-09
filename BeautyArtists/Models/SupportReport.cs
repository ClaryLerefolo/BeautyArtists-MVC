namespace BeautyArtists.Models
{
    public class SupportReport
    {
        public int Id { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public string Email { get; set; }
        public DateTime SubmittedAt { get; set; }
    }
}
