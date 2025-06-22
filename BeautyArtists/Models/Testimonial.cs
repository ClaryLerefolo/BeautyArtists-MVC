using System;

namespace BeautyArtists.Models
{
    public class Testimonial
    {
        public int Id { get; set; }
        public string CustomerName { get; set; }
        public string Message { get; set; }
        public string PhotoUrl { get; set; } // Optional photo of customer
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
