using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BeautyArtists.Models.ViewModels
{
    public class BankingViewModel
    {
        [Required(ErrorMessage = "Please select your bank.")]
        public string BankCode { get; set; } = "";

        public string BankName { get; set; } = "";

        [Required(ErrorMessage = "Account number is required.")]
        [RegularExpression(@"^[0-9]{8,13}$",
            ErrorMessage = "Account number must be between 8 and 13 digits.")]
        public string AccountNumber { get; set; } = "";

        // FIX: Not required on the ViewModel — Paystack fills this in.
        //      We just store and display it.
        public string? AccountHolderName { get; set; }

        public bool IsBankAccountVerified { get; set; }
        public string? SubaccountCode { get; set; }

        public List<SelectListItem> Banks { get; set; } = new();
    }
}