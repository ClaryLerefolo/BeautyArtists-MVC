using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;



namespace BeautyArtists.Models
{
    public class ArtistProfile
    {

        public int Id { get; set; }
        public string UserId { get; set; } = null!;

        public ApplicationUser User { get; set; } // <-- Add this line
        [Required]
        [Display(Name ="Full Name")]
        public string FullName { get; set; }

     

        [Required]
        public string Bio { get; set; }

        [Required]
        [Display(Name = "Years' Experience")]
        public int YearsExperience { get; set; } = 0;
        [Required]
        public string Location { get; set; }

        [Required]
        [Display(Name = "Contact Info")]
        [RegularExpression(@"^0\d{2}-\d{3}-\d{4}$", ErrorMessage = "Enter a valid South African phone number like 082-123-4567.")]


        public string ContactInfo { get; set; }

        [Display(Name = " Profile Picture Url ")]

        public string ProfilePictureUrl { get; set; }
        [Required]
        [Display(Name = " Instagram Url ")]

        public string InstagramUrl { get; set; }

        public ICollection<Service> Services { get; set; }





    }
}
