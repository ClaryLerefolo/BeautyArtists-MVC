using System.Threading.Tasks;

namespace BeautyArtists.Services
{
    public interface ICommunicationService
    {
        // System-triggered booking notifications
        Task SendBookingConfirmationToClientAsync(string clientId, int bookingId);
        Task SendBookingRequestToArtistAsync(string artistId, int bookingId);
        Task SendBookingStatusUpdateAsync(string recipientId, int bookingId, string status);

        // Direct user-to-user communication messaging wrapper
        Task SendDirectMessageEmailAsync(string senderId, string recipientId, string messageSubject, string messageBody);
    }
}