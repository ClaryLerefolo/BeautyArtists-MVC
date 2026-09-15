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
    public class RegisterConfirmationModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;

        public RegisterConfirmationModel(
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender)
        {
            _userManager = userManager;
            _emailSender = emailSender;
        }

        public string Email { get; set; }
        public string StatusMessage { get; set; }

        public async Task<IActionResult> OnGetAsync(string email)
        {
            if (string.IsNullOrEmpty(email))
                return RedirectToPage("/Index");

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
                return NotFound($"Unable to load user with email '{email}'.");

            Email = email;
            return Page();
        }

        public async Task<IActionResult> OnPostResendEmailAsync(string email)
        {
            if (string.IsNullOrEmpty(email))
                return RedirectToPage("/Index");

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null || user.EmailConfirmed)
            {
                StatusMessage = "Unable to resend confirmation email.";
                Email = email;
                return Page();
            }

            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));

            var callbackUrl = Url.Page(
                "/Account/ConfirmEmail",
                pageHandler: null,
                values: new { area = "Identity", userId = user.Id, code = code },
                protocol: Request.Scheme,
                host: Request.Host.Value);

            await _emailSender.SendEmailAsync(
                email,
                "Confirm your RubiOr Account",
                $"<h3>Welcome back!</h3><p>Please confirm your account by <a href='{callbackUrl}'>clicking here</a>.</p>");

            StatusMessage = "Confirmation email resent. Check your inbox and spam folder.";
            Email = email;
            return Page();
        }
    }
}