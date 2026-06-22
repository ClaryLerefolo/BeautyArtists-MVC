using BeautyArtists.Models.ViewModels;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BeautyArtists.Services
{
    public interface IChatService
    {
        Task<List<ChatUserListItem>> GetChatUsersAsync(string userId);
        Task<int> GetUnreadCountAsync(string userId);
        Task MarkAllAsReadAsync(string userId, string senderId);
    }
}