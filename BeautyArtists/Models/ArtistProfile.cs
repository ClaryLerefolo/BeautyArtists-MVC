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

        [NotMapped]
        public string ArtistName => FullName; 

        [Required]
        public string Bio { get; set; }

        [Required]
        [Display(Name = "Years' Experience")]
        public int YearsExperience { get; set; } = 0;
        [Required]
        public string Province { get; set; }
        [Required]
        public string City { get; set; }

        [Required]
        [Display(Name = "Contact Info")]
        [RegularExpression(@"^0\d{2}-\d{3}-\d{4}$", ErrorMessage = "Enter a valid South African phone number like 082-123-4567.")]


        public string ContactInfo { get; set; }

        [Display(Name = " Profile Picture Url ")]

        public string ProfilePictureUrl { get; set; }
        [Required]
        [Display(Name = " Instagram Url ")]

        public string InstagramUrl { get; set; }
        public string? FacebookUrl { get; set; }
        public string? TwitterUrl { get; set; }
        public string? TikTokUrl { get; set; }

        public ICollection<Service> Services { get; set; }

        // ─── 🔥 NEW: BANKING & PAYSTACK FIELDS ───
        public string? BankName { get; set; }     // e.g., "Capitec", "FNB"

        public string? BankCode { get; set; }
        public string? AccountHolderName { get; set; }     // e.g., "Clary"
        public string? SubaccountCode { get; set; }        // e.g., "ACCT_xxxxxxxxxx" (from Paystack)
        public bool IsBankAccountVerified { get; set; }    // true = verified & subaccount created
        public DateTime? BankAccountVerifiedDate { get; set; }
        public string? StudioAddress { get; set; }          // e.g. "123 Main St, Sandton"
        public string? StudioCity { get; set; }
        public string? StudioProvince { get; set; }
        public string? StudioPostalCode { get; set; }
        public double? StudioLatitude { get; set; }
        public double? StudioLongitude { get; set; }



    }
}
