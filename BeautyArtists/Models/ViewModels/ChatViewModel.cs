using System.Collections.Generic;

namespace BeautyArtists.Models.ViewModels
{
    public class ChatViewModel
    {
        public string ReceiverId { get; set; }
        public string ReceiverName { get; set; }
        public string ReceiverProfilePicture { get; set; }
        public string CurrentUserId { get; set; }
        public string CurrentUserName { get; set; }
        public int? BookingId { get; set; }
        public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    }

    public class ChatUserListItem
    {
        public string UserId { get; set; }
        public string Name { get; set; }
        public string ProfilePicture { get; set; }
        public string LastMessage { get; set; }
        public DateTime? LastMessageTime { get; set; }
        public int UnreadCount { get; set; }
        public bool IsOnline { get; set; }
    }
}