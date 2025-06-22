using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using static BeautyArtists.Models.UserService;

namespace BeautyArtists.Models
{
    public class ApplicationUser :IdentityUser
    {
      
        public string FirstName { get; set; } = string.Empty;//To STore User's Full Name\
        public string LastName { get; set; } = string.Empty;
        public string FullName  => $"{FirstName} {LastName}";
        public string? Role { get; set; } = string.Empty; //To store role chosen during registration (Client or Artist)
        public ICollection<UserService>? UserServices { get; set; }

        public ArtistProfile ArtistProfile { get; set; }


    }
}
