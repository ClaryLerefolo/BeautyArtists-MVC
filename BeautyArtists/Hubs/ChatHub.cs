using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Services;
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
        private readonly IChatService _chatService;


        public ChatHub(ApplicationDbContext context, IChatService chatService)
        {
            _context = context;
            _chatService = chatService;
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

        public override async Task OnDisconnectedAsync(Exception? exception)
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
                throw new HubException("Sender not authenticated – your user ID is missing.");

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

                await Clients.User(receiverId).SendAsync("ReceiveMessage", new
                {
                    id = chatMessage.Id,
                    senderId,
                    message,
                    sentAt = chatMessage.SentAt,
                    isRead = false,
                    bookingId
                });

                await Clients.User(senderId).SendAsync("MessageSent", new
                {
                    id = chatMessage.Id,
                    receiverId,
                    message,
                    sentAt = chatMessage.SentAt,
                    isRead = false,
                    bookingId
                });
            }
            catch (Exception ex)
            {
                throw new HubException($"DB error: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        // ─── TYPING INDICATORS ───
        public async Task Typing(string receiverId)
        {
            var senderId = Context.UserIdentifier;
            if (!string.IsNullOrEmpty(senderId))
                await Clients.User(receiverId).SendAsync("UserTyping", senderId);
        }

        public async Task StopTyping(string receiverId)
        {
            var senderId = Context.UserIdentifier;
            if (!string.IsNullOrEmpty(senderId))
                await Clients.User(receiverId).SendAsync("UserStoppedTyping", senderId);
        }
        // ─── OTHER METHODS ───
        public async Task MarkAsRead(int messageId)
        {
            var userId = Context.UserIdentifier;
            var message = await _context.ChatMessages.FindAsync(messageId);

            if (message != null && message.ReceiverId == userId && !message.IsRead)
            {
                message.IsRead = true;
                message.ReadAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                await Clients.User(message.SenderId).SendAsync("MessageRead", messageId);
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
            await Clients.User(senderId).SendAsync("MessagesRead", userId);
        }

        public async Task GetChatHistory(string otherUserId, int? bookingId = null, int skip = 0, int take = 50)
        {
            var userId = Context.UserIdentifier;
            var query = _context.ChatMessages
                .Where(m => (m.SenderId == userId && m.ReceiverId == otherUserId) ||
                            (m.SenderId == otherUserId && m.ReceiverId == userId));

            if (bookingId.HasValue)
                query = query.Where(m => m.BookingId == bookingId);

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