using BeautyArtists.Data;
using BeautyArtists.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;

namespace BeautyArtists.Hubs
{
    [Authorize]
    public class ChatHub : Hub
    {
        private readonly ApplicationDbContext _context;

        public ChatHub(ApplicationDbContext context)
        {
            _context = context;
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.UserIdentifier;
            if (!string.IsNullOrEmpty(userId))
            {
                // Track online status
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");

                // Update online status
                await Clients.Group($"user_{userId}").SendAsync("UserOnline", userId, true);
            }
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var userId = Context.UserIdentifier;
            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");
                await Clients.Group($"user_{userId}").SendAsync("UserOnline", userId, false);
            }
            await base.OnDisconnectedAsync(exception);
        }

        public async Task SendMessage(string receiverId, string message, int? bookingId = null)
        {
            var senderId = Context.UserIdentifier;

            if (string.IsNullOrEmpty(senderId) || string.IsNullOrEmpty(receiverId) || string.IsNullOrEmpty(message))
                return;

            // Save message to database
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

            // Send to receiver (real-time)
            await Clients.Group($"user_{receiverId}").SendAsync("ReceiveMessage", new
            {
                id = chatMessage.Id,
                senderId = chatMessage.SenderId,
                message = chatMessage.Message,
                sentAt = chatMessage.SentAt,
                isRead = chatMessage.IsRead,
                bookingId = chatMessage.BookingId
            });

            // Also send back to sender for confirmation
            await Clients.Group($"user_{senderId}").SendAsync("MessageSent", new
            {
                id = chatMessage.Id,
                receiverId = chatMessage.ReceiverId,
                message = chatMessage.Message,
                sentAt = chatMessage.SentAt,
                isRead = chatMessage.IsRead,
                bookingId = chatMessage.BookingId
            });
        }

        public async Task MarkAsRead(int messageId)
        {
            var userId = Context.UserIdentifier;
            var message = await _context.ChatMessages.FindAsync(messageId);

            if (message != null && message.ReceiverId == userId && !message.IsRead)
            {
                message.IsRead = true;
                message.ReadAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                await Clients.Group($"user_{message.SenderId}").SendAsync("MessageRead", messageId);
            }
        }

        public async Task MarkAllAsRead(string senderId)
        {
            var userId = Context.UserIdentifier;
            var messages = await _context.ChatMessages
                .Where(m => m.SenderId == senderId && m.ReceiverId == userId && !m.IsRead)
                .ToListAsync();

            foreach (var msg in messages)
            {
                msg.IsRead = true;
                msg.ReadAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            await Clients.Group($"user_{senderId}").SendAsync("MessagesRead", userId);
        }

        public async Task GetChatHistory(string otherUserId, int? bookingId = null, int skip = 0, int take = 50)
        {
            var userId = Context.UserIdentifier;

            var query = _context.ChatMessages
                .Where(m => (m.SenderId == userId && m.ReceiverId == otherUserId) ||
                            (m.SenderId == otherUserId && m.ReceiverId == userId));

            if (bookingId.HasValue)
            {
                query = query.Where(m => m.BookingId == bookingId);
            }

            var messages = await query
                .OrderByDescending(m => m.SentAt)
                .Skip(skip)
                .Take(take)
                .OrderBy(m => m.SentAt)
                .ToListAsync();

            await Clients.Caller.SendAsync("ChatHistory", messages);
        }

        public async Task GetUnreadCount()
        {
            var userId = Context.UserIdentifier;
            var count = await _context.ChatMessages
                .Where(m => m.ReceiverId == userId && !m.IsRead)
                .CountAsync();

            await Clients.Caller.SendAsync("UnreadCount", count);
        }
    }
}