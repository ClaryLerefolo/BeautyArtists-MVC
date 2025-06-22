using System.ComponentModel.DataAnnotations;

namespace BeautyArtists.Models.ViewModels
{
    public class BrowseArtistViewModel
    {
        public string ArtistId { get; set; }

        [Display(Name = "Full Name")]
        public string FullName { get; set; }
       
        public string Location { get; set; }

        public string ProfilePictureUrl { get; set; }

        public List<string> Services { get; set; }

    }
}
