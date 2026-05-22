using System.ComponentModel.DataAnnotations;

namespace BeautyArtists.Models.ViewModels
{
    public class BrowseArtistViewModel
    {
        public string ArtistId { get; set; }

        [Display(Name = "Full Name")]
        public string FullName { get; set; }
       
        public string Province { get; set; }
        public string City { get; set; }


        public string ProfilePictureUrl { get; set; }

        public List<string> Services { get; set; }

        public string? ContactInfo { get; set; }
        public string? InstagramUrl { get; set; }
        public int YearsExperience { get; set; }
        public string? Bio { get; set; }

    }
}
