using System.ComponentModel.DataAnnotations;

namespace BeautyArtists.Models
{
    public class PortfolioCategory
    {
        public int Id { get; set; }

        [Required, StringLength(100)]
        public string Name { get; set; }

    }
}
