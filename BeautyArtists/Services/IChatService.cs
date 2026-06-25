using BeautyArtists.Models;
using BeautyArtists.Models.ViewModels;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BeautyArtists.Services
{
    public interface IChatService
    {
        // Get list of users the current user has chatted with
        Task<List<ChatUserListItem>> GetChatUsersAsync(string userId);

        // Get total unread count for a user (all senders)
        Task<int> GetUnreadCountAsync(string userId);

        // Get unread count between two specific users
        Task<int> GetUnreadCountBetweenUsersAsync(string userId, string otherUserId);

        // Get conversation messages between two users, optionally filtered by booking
        Task<List<ChatMessage>> GetMessagesAsync(string userId, string otherUserId, int? bookingId = null);

        // Send a new message and return the saved entity
        Task<ChatMessage> SendMessageAsync(string senderId, string receiverId, string message, int? bookingId = null);

        // Mark all messages from a specific sender as read for the current user
        Task MarkAllAsReadAsync(string userId, string senderId);

        // Mark a single message as read
        Task MarkAsReadAsync(int messageId, string userId);
    }
}