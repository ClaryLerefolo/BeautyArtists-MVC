using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Models.ViewModels;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BeautyArtists.Services
{
    public class ChatService : IChatService
    {
        private readonly ApplicationDbContext _context;

        public ChatService(ApplicationDbContext context)
        {
            _context = context;
        }

        // ---------- Existing methods (unchanged) ----------
        public async Task<List<ChatUserListItem>> GetChatUsersAsync(string userId)
        {
            var chatUserIds = await _context.ChatMessages
                .Where(m => m.SenderId == userId || m.ReceiverId == userId)
                .Select(m => m.SenderId == userId ? m.ReceiverId : m.SenderId)
                .Distinct()
                .ToListAsync();

            var users = new List<ChatUserListItem>();

            foreach (var id in chatUserIds)
            {
                var user = await _context.Users
                    .Include(u => u.ArtistProfile)
                    .FirstOrDefaultAsync(u => u.Id == id);

                if (user == null) continue;

                var lastMessage = await _context.ChatMessages
                    .Where(m => (m.SenderId == userId && m.ReceiverId == id) ||
                                (m.SenderId == id && m.ReceiverId == userId))
                    .OrderByDescending(m => m.SentAt)
                    .FirstOrDefaultAsync();

                var unreadCount = await _context.ChatMessages
                    .Where(m => m.SenderId == id && m.ReceiverId == userId && !m.IsRead)
                    .CountAsync();

                users.Add(new ChatUserListItem
                {
                    UserId = user.Id,
                    Name = !string.IsNullOrEmpty(user.FirstName)
                        ? $"{user.FirstName} {user.LastName}".Trim()
                        : user.UserName ?? "User",
                    ProfilePicture = user.ArtistProfile?.ProfilePictureUrl ?? "/images/default-profile.png",
                    LastMessage = lastMessage?.Message,
                    LastMessageTime = lastMessage?.SentAt,
                    UnreadCount = unreadCount,
                    IsOnline = false
                });
            }

            return users.OrderByDescending(u => u.LastMessageTime).ToList();
        }

        public async Task<int> GetUnreadCountAsync(string userId)
        {
            return await _context.ChatMessages
                .Where(m => m.ReceiverId == userId && !m.IsRead)
                .CountAsync();
        }

        public async Task MarkAllAsReadAsync(string userId, string senderId)
        {
            var messages = await _context.ChatMessages
                .Where(m => m.SenderId == senderId && m.ReceiverId == userId && !m.IsRead)
                .ToListAsync();

            foreach (var msg in messages)
            {
                msg.IsRead = true;
                msg.ReadAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
        }

        // ---------- NEW: Missing implementations ----------

        /// <summary>
        /// Returns the number of unread messages sent by otherUserId to userId.
        /// </summary>
        public async Task<int> GetUnreadCountBetweenUsersAsync(string userId, string otherUserId)
        {
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(otherUserId))
                return 0;

            return await _context.ChatMessages
                .Where(m => m.SenderId == otherUserId && m.ReceiverId == userId && !m.IsRead)
                .CountAsync();
        }

        /// <summary>
        /// Retrieves all messages between two users, optionally filtered by a specific booking.
        /// Ordered by SentAt ascending (oldest first).
        /// </summary>
        public async Task<List<ChatMessage>> GetMessagesAsync(string userId, string otherUserId, int? bookingId = null)
        {
            var query = _context.ChatMessages
                .Where(m => (m.SenderId == userId && m.ReceiverId == otherUserId) ||
                            (m.SenderId == otherUserId && m.ReceiverId == userId));

            if (bookingId.HasValue)
                query = query.Where(m => m.BookingId == bookingId.Value);

            return await query
                .OrderBy(m => m.SentAt)   // ascending: oldest first
                .ToListAsync();
        }

        /// <summary>
        /// Sends a new message, saves it, and returns the created ChatMessage entity.
        /// </summary>
        public async Task<ChatMessage> SendMessageAsync(string senderId, string receiverId, string message, int? bookingId = null)
        {
            if (string.IsNullOrEmpty(senderId) || string.IsNullOrEmpty(receiverId) || string.IsNullOrEmpty(message))
                throw new ArgumentException("Sender, receiver, and message content are required.");

            var chatMessage = new ChatMessage
            {
                SenderId = senderId,
                ReceiverId = receiverId,
                Message = message,
                BookingId = bookingId,
                SentAt = DateTime.UtcNow,
                IsRead = false
            };

            _context.ChatMessages.Add(chatMessage);
            await _context.SaveChangesAsync();

            return chatMessage;
        }

        /// <summary>
        /// Marks a single message as read, but only if the current user is the intended receiver.
        /// </summary>
        public async Task MarkAsReadAsync(int messageId, string userId)
        {
            if (string.IsNullOrEmpty(userId))
                throw new ArgumentNullException(nameof(userId));

            var message = await _context.ChatMessages.FirstOrDefaultAsync(m => m.Id == messageId);
            if (message == null)
                return; // or throw, depending on your preference

            // Security check: only the receiver can mark as read
            if (message.ReceiverId != userId)
                return; // or throw UnauthorizedAccessException

            if (!message.IsRead)
            {
                message.IsRead = true;
                message.ReadAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }
    }
}