using BeautyArtists.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BeautyArtists.Services
{
    public interface INotificationService
    {
        // Create a new notification
        Task<Notification> CreateNotificationAsync(
            string userId,
            string title,
            string message,
            string type,
            string referenceId = null,
            string actionUrl = null);

        // Mark a single notification as read
        Task MarkAsReadAsync(int notificationId, string userId);

        // Mark all notifications as read for a user
        Task MarkAllAsReadAsync(string userId);

        // Delete a notification
        Task DeleteNotificationAsync(int notificationId, string userId);

        // Delete all notifications for a user
        Task DeleteAllNotificationsAsync(string userId);

        // Get all notifications for a user
        Task<List<Notification>> GetUserNotificationsAsync(string userId, int limit = 50);

        // Get unread notifications count for a user
        Task<int> GetUnreadCountAsync(string userId);

        // Get a single notification by ID
        Task<Notification> GetNotificationByIdAsync(int notificationId);

        // Send bulk notifications to multiple users
        Task SendBulkNotificationsAsync(List<string> userIds, string title, string message, string type, string referenceId = null);

        // Send notification to all artists
        Task NotifyAllArtistsAsync(string title, string message, string type, string referenceId = null);

        // Send notification to all clients
        Task NotifyAllClientsAsync(string title, string message, string type, string referenceId = null);
    }
}