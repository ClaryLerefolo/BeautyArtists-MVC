using System.Collections.Generic;

namespace BeautyArtists.Models.ViewModels
{
    public class MyReviewsViewModel
    {
        public List<ReviewWithDetails> Reviews { get; set; }
    }

    public class ReviewWithDetails
    {
        public Review Review { get; set; }
        public string ServiceName { get; set; }
        public string ArtistName { get; set; }
        public string ArtistProfilePicture { get; set; }
        public string AppointmentDate { get; set; }
        public string ArtistId { get; set; }
    }
}