using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BeautyArtists.Models.ViewModels
{
    public class BankingViewModel
    {
        [Required(ErrorMessage = "Please select your bank.")]
        public string BankCode { get; set; }

        public string BankName { get; set; }

        [Required(ErrorMessage = "Account number is required.")]
        [RegularExpression(@"^[0-9]+$", ErrorMessage = "Account number must contain only digits.")]
        public string AccountNumber { get; set; }

        public string? AccountHolderName { get; set; }

        public bool IsBankAccountVerified { get; set; }
        public string? SubaccountCode { get; set; }

        public List<SelectListItem> Banks { get; set; } = new List<SelectListItem>();
    }
}