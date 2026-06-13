using BeautyArtists.Data;
using BeautyArtists.Hubs;
using BeautyArtists.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BeautyArtists.Services
{
    public class NotificationService : INotificationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly UserManager<ApplicationUser> _userManager;

        public NotificationService(
            ApplicationDbContext context,
            IHubContext<NotificationHub> hubContext,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _hubContext = hubContext;
            _userManager = userManager;
        }

        public async Task<Notification> CreateNotificationAsync(
            string userId,
            string title,
            string message,
            string type,
            string referenceId = null,
            string actionUrl = null)
        {
            var notification = new Notification
            {
                UserId = userId,
                Title = title,
                Message = message,
                Type = type,
                ReferenceId = referenceId,
                ActionUrl = actionUrl,
                Icon = GetIconForType(type),
                CreatedAt = DateTime.UtcNow,
                IsRead = false
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            // Send real-time notification via SignalR
            await _hubContext.Clients.Group($"user_{userId}").SendAsync("ReceiveNotification", new
            {
                notification.Id,
                notification.Title,
                notification.Message,
                notification.Type,
                notification.Icon,
                notification.CreatedAt,
                notification.ActionUrl
            });

            return notification;
        }

        public async Task MarkAsReadAsync(int notificationId, string userId)
        {
            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);

            if (notification != null && !notification.IsRead)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                // Notify client that notification was marked as read
                await _hubContext.Clients.Group($"user_{userId}").SendAsync("NotificationRead", notificationId);
            }
        }

        public async Task MarkAllAsReadAsync(string userId)
        {
            var notifications = await _context.Notifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .ToListAsync();

            foreach (var notification in notifications)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            await _hubContext.Clients.Group($"user_{userId}").SendAsync("AllNotificationsRead");
        }

        public async Task DeleteNotificationAsync(int notificationId, string userId)
        {
            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);

            if (notification != null)
            {
                _context.Notifications.Remove(notification);
                await _context.SaveChangesAsync();

                await _hubContext.Clients.Group($"user_{userId}").SendAsync("NotificationDeleted", notificationId);
            }
        }

        public async Task DeleteAllNotificationsAsync(string userId)
        {
            var notifications = await _context.Notifications
                .Where(n => n.UserId == userId)
                .ToListAsync();

            _context.Notifications.RemoveRange(notifications);
            await _context.SaveChangesAsync();

            await _hubContext.Clients.Group($"user_{userId}").SendAsync("AllNotificationsDeleted");
        }

        public async Task<List<Notification>> GetUserNotificationsAsync(string userId, int limit = 50)
        {
            return await _context.Notifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(limit)
                .ToListAsync();
        }

        public async Task<int> GetUnreadCountAsync(string userId)
        {
            return await _context.Notifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .CountAsync();
        }

        public async Task<Notification> GetNotificationByIdAsync(int notificationId)
        {
            return await _context.Notifications
                .FirstOrDefaultAsync(n => n.Id == notificationId);
        }

        public async Task SendBulkNotificationsAsync(List<string> userIds, string title, string message, string type, string referenceId = null)
        {
            var notifications = new List<Notification>();
            var now = DateTime.UtcNow;
            var icon = GetIconForType(type);

            foreach (var userId in userIds)
            {
                notifications.Add(new Notification
                {
                    UserId = userId,
                    Title = title,
                    Message = message,
                    Type = type,
                    ReferenceId = referenceId,
                    Icon = icon,
                    CreatedAt = now,
                    IsRead = false
                });
            }

            await _context.Notifications.AddRangeAsync(notifications);
            await _context.SaveChangesAsync();

            // Send real-time notifications to each user
            foreach (var userId in userIds)
            {
                await _hubContext.Clients.Group($"user_{userId}").SendAsync("ReceiveNotification", new
                {
                    Title = title,
                    Message = message,
                    Type = type,
                    Icon = icon,
                    CreatedAt = now
                });
            }
        }

        public async Task NotifyAllArtistsAsync(string title, string message, string type, string referenceId = null)
        {
            // Get all users with Artist role
            var artists = await _userManager.GetUsersInRoleAsync("Artist");
            var artistIds = artists.Select(a => a.Id).ToList();

            await SendBulkNotificationsAsync(artistIds, title, message, type, referenceId);
        }

        public async Task NotifyAllClientsAsync(string title, string message, string type, string referenceId = null)
        {
            // Get all users with Client role
            var clients = await _userManager.GetUsersInRoleAsync("Client");
            var clientIds = clients.Select(c => c.Id).ToList();

            await SendBulkNotificationsAsync(clientIds, title, message, type, referenceId);
        }

        private string GetIconForType(string type)
        {
            return type switch
            {
                "booking_accepted" => "✅",
                "booking_pending" => "⏳",
                "booking_confirmed" => "🎉",
                "booking_cancelled" => "❌",
                "booking_rejected" => "🚫",
                "payment_received" => "💰",
                "payment_deposit" => "💳",
                "payment_final" => "💵",
                "reminder" => "🔔",
                "new_message" => "💬",
                "review_received" => "⭐",
                "system_alert" => "⚠️",
                "welcome" => "👋",
                "promotion" => "🎁",
                _ => "📢"
            };
        }
    }
}