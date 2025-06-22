namespace BeautyArtists.Models
{
    public class ServiceImage
    {
        public int Id { get; set; }
        public int UserServiceId { get; set; }
        public string ImagePath { get; set; } = string.Empty;

        public UserService UserService { get; set; }
    }

}
