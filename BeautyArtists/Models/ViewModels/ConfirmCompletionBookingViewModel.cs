using System;

namespace BeautyArtists.Models.ViewModels
{
    public class ConfirmCompletionViewModel
    {
        public int BookingId { get; set; }
        public string? ServiceName { get; set; }
        public string? ArtistName { get; set; }
        public DateTime AppointmentDate { get; set; }
    }
}