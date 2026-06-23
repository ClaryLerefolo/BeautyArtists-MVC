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
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
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

            if (string.IsNullOrEmpty(senderId))
                throw new HubException("Sender not authenticated.");

            if (string.IsNullOrEmpty(receiverId))
                throw new HubException("Receiver ID is required.");

            if (string.IsNullOrEmpty(message))
                throw new HubException("Message cannot be empty.");

            try
            {
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

                // Notify receiver
                await Clients.User(receiverId).SendAsync("ReceiveMessage", new
                {
                    id = chatMessage.Id,
                    senderId = chatMessage.SenderId,
                    message = chatMessage.Message,
                    sentAt = chatMessage.SentAt,
                    isRead = chatMessage.IsRead,
                    bookingId = chatMessage.BookingId
                });

                // Confirm to sender
                await Clients.User(senderId).SendAsync("MessageSent", new
                {
                    id = chatMessage.Id,
                    receiverId = chatMessage.ReceiverId,
                    message = chatMessage.Message,
                    sentAt = chatMessage.SentAt,
                    isRead = chatMessage.IsRead,
                    bookingId = chatMessage.BookingId
                });
            }
            catch (Exception ex)
            {
                // 🔥 This will send the REAL error to the client
                var errorMessage = ex.InnerException?.Message ?? ex.Message;

                throw new HubException($"Database error: {ex.InnerException?.Message ?? ex.Message}");
            }
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