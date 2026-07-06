using System.Collections.Generic;
using BeautyArtists.Models;

namespace BeautyArtists.Models.ViewModels
{
    public class HomeViewModel
    {
        public IEnumerable<UserService> FeaturedServices { get; set; }
        public List<Testimonial> Testimonials { get; set; }
        public IEnumerable<HeroBanner> Banners { get; set; } = new List<HeroBanner>();
        public string HeroImagePath { get; set; }
        public IEnumerable<ServiceCategory> Categories { get; set; } = new List<ServiceCategory>();
        public List<TopRatedService> TopRatedServices { get; set; }

    }
    public class TopRatedService
    {
        public UserService Service { get; set; }
        public double AverageRating { get; set; }
        public int ReviewCount { get; set; }
    }
}
