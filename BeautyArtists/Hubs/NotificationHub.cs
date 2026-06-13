using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using System.Threading.Tasks;

namespace BeautyArtists.Hubs
{
    [Authorize]
    public class NotificationHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            // Get the logged-in user's ID
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!string.IsNullOrEmpty(userId))
            {
                // Add user to their personal group for targeted notifications
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!string.IsNullOrEmpty(userId))
            {
                // Remove user from group when they disconnect
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");
            }

            await base.OnDisconnectedAsync(exception);
        }

        // Server method to send notification to a specific user
        public async Task SendToUser(string userId, object notification)
        {
            await Clients.Group($"user_{userId}").SendAsync("ReceiveNotification", notification);
        }

        // Server method to send notification to all connected clients (admin broadcast)
        public async Task SendToAll(string notification)
        {
            await Clients.All.SendAsync("BroadcastNotification", notification);
        }

        // Client calls this to acknowledge they've seen a notification
        public async Task MarkAsRead(int notificationId)
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            // Notify all of this user's devices that a notification was marked as read
            await Clients.Group($"user_{userId}").SendAsync("NotificationRead", notificationId);
        }
    }
}