using BeautyArtists.Data;
using BeautyArtists.Models;
using BeautyArtists.Models.ViewModels;
using BeautyArtists.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace BeautyArtists.Controllers
{
    [Authorize]
    public class ChatController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IChatService _chatService;

        public ChatController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IChatService chatService)
        {
            _context = context;
            _userManager = userManager;
            _chatService = chatService;
        }

        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User);
            var users = await _chatService.GetChatUsersAsync(userId);

            ViewData["Layout"] = User.IsInRole("Artist")
                ? "~/Views/Shared/_ArtistLayout.cshtml"
                : "~/Views/Shared/_Layout.cshtml";

            return View(users);
        }

        public async Task<IActionResult> Conversation(string userId, int? bookingId)
        {
            var currentUserId = _userManager.GetUserId(User);

            var otherUser = await _context.Users
                .Include(u => u.ArtistProfile)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (otherUser == null) return NotFound();

            var messages = await _context.ChatMessages
                .Where(m => (m.SenderId == currentUserId && m.ReceiverId == userId) ||
                            (m.SenderId == userId && m.ReceiverId == currentUserId))
                .OrderBy(m => m.SentAt)
                .ToListAsync();

            var model = new ChatViewModel
            {
                ReceiverId = userId,
                ReceiverName = !string.IsNullOrEmpty(otherUser.FirstName)
                    ? $"{otherUser.FirstName} {otherUser.LastName}".Trim()
                    : otherUser.UserName ?? "User",
                ReceiverProfilePicture = otherUser.ArtistProfile?.ProfilePictureUrl ?? "/images/default-profile.png",
                CurrentUserId = currentUserId,
                CurrentUserName = User.Identity.Name,
                BookingId = bookingId,
                Messages = messages
            };

            ViewData["Layout"] = User.IsInRole("Artist")
                ? "~/Views/Shared/_ArtistLayout.cshtml"
                : "~/Views/Shared/_Layout.cshtml";

            await _chatService.MarkAllAsReadAsync(currentUserId, userId);

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> GetUnreadCount()
        {
            var userId = _userManager.GetUserId(User);
            var count = await _chatService.GetUnreadCountAsync(userId);
            return Json(new { count });
        }
    }
}