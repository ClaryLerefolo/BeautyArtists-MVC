using BeautyArtists.Data;
using BeautyArtists.Models;
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

        public NotificationService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Notification> CreateNotificationAsync(
            string userId,
            string title,
            string message,
            string type,
            string? referenceId = null,
            string? actionUrl = null)
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

            // ❌ SignalR removed – no real‑time push

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
        }

        public async Task DeleteNotificationAsync(int notificationId, string userId)
        {
            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);

            if (notification != null)
            {
                _context.Notifications.Remove(notification);
                await _context.SaveChangesAsync();
            }
        }

        public async Task DeleteAllNotificationsAsync(string userId)
        {
            var notifications = await _context.Notifications
                .Where(n => n.UserId == userId)
                .ToListAsync();

            _context.Notifications.RemoveRange(notifications);
            await _context.SaveChangesAsync();
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

        public async Task SendBulkNotificationsAsync(
            List<string> userIds,
            string title,
            string message,
            string type,
            string? referenceId = null)
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

            // ❌ SignalR removed – no real‑time pushes
        }

        public async Task NotifyAllArtistsAsync(string title, string message, string type, string? referenceId = null)
        {
            // You’ll need to inject UserManager<ApplicationUser> or RoleManager
            // For now, we’ll leave it as a placeholder – you can implement later.
            // This method is not used in the current flow, so it's okay to leave it as a stub.
            // If you need it, add UserManager dependency and fetch artist user IDs.
            throw new NotImplementedException("NotifyAllArtistsAsync requires UserManager to fetch artists.");
        }

        public async Task NotifyAllClientsAsync(string title, string message, string type, string? referenceId = null)
        {
            // Same as above – implement if needed.
            throw new NotImplementedException("NotifyAllClientsAsync requires UserManager to fetch clients.");
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