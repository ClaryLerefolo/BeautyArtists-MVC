namespace BeautyArtists.Models.ViewModels
{
    public class CustomerAppointmentViewModel
    {
        public int Id { get; set; }

        public string ServiceName { get; set; }
        public string ArtistName { get; set; }

        public DateTime AppointmentDate { get; set; }
        public TimeSpan AppointmentTime { get; set; }

        public string Notes { get; set; }
        public bool IsConfirmed { get; set; }
    }
}
