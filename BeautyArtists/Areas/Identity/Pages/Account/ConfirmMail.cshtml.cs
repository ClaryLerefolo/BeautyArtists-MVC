using System;
using System.Text;
using System.Threading.Tasks;
using BeautyArtists.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace BeautyArtists.Areas.Identity.Pages.Account
{
    [AllowAnonymous]
    public class ConfirmEmailModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;

        public ConfirmEmailModel(
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender)
        {
            _userManager = userManager;
            _emailSender = emailSender;
        }

        [TempData]
        public string StatusMessage { get; set; }

        public async Task<IActionResult> OnGetAsync(string userId, string code)
        {
            if (userId == null || code == null)
                return RedirectToPage("/Index");

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound($"Unable to load user with ID '{userId}'.");

            code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            var result = await _userManager.ConfirmEmailAsync(user, code);

            if (!result.Succeeded)
            {
                StatusMessage = "Error confirming your email.";
                return Page();
            }

            StatusMessage = "Thank you for confirming your email.";

            // ✅ Send welcome email to artists only
            try
            {
                var roles = await _userManager.GetRolesAsync(user);
                Console.WriteLine($"🔍 ConfirmEmail: User={user.Email}, Roles={string.Join(",", roles)}");

                if (roles.Contains("Artist"))
                {
                    var uploadUrl = Url.Action(
                        "ManageServices",
                        "Artist",
                        null,
                        protocol: Request.Scheme,
                        host: Request.Host.Value);

                    Console.WriteLine($"📧 Sending welcome email to {user.Email}, URL={uploadUrl}");

                    await _emailSender.SendEmailAsync(
                        user.Email,
                        "Welcome to RubiOr — Time to Upload Your Content",
                        $"<h3>Welcome, {user.FirstName}!</h3>" +
                        $"<p>Your email is confirmed and your artist account is now active.</p>" +
                        $"<p>The next step is to set up your services so clients can find you and book you.</p>" +
                        $"<p>Please upload your content by <a href='{uploadUrl}'>clicking here</a>.</p>" +
                        $"<p>You can add your services, prices, images, and availability from there.</p>");

                    Console.WriteLine($"✅ Welcome email sent to {user.Email}");
                }
                else
                {
                    Console.WriteLine($"⚠️ User {user.Email} is NOT an Artist — no welcome email sent.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Welcome email failed for {user.Email}: {ex.Message}");
                Console.WriteLine($"📚 Stack: {ex.StackTrace}");
            }

            return Page();
        }
    }
}