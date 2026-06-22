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

        public async Task<List<ChatUserListItem>> GetChatUsersAsync(string userId)
        {
            // Get all users the current user has chatted with
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
                    IsOnline = false // Could be tracked with SignalR
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
    }
}