using System;
using System.ComponentModel.DataAnnotations;

namespace BeautyArtists.Models
{
    public class HeroBackground
    {
        public int Id { get; set; }

        [Required]
        public string ImagePath { get; set; } = string.Empty;

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }
}
