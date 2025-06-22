using System.Collections.Generic;
using BeautyArtists.Models;

namespace BeautyArtists.Models.ViewModels
{
    public class HomeViewModel
    {
        public List<Service> FeaturedServices { get; set; }
        public List<Testimonial> Testimonials { get; set; }
        public string HeroImagePath { get; set; } // e.g., "uploads/hero/hero1.jpg"

    }
}
