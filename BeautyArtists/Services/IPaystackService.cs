using BeautyArtists.Models.ViewModels;

namespace BeautyArtists.Services
{
    public interface IPaystackService
    {
        Task<BankValidationResult> ValidateBankAccountAsync(string bankCode, string accountNumber);
        Task<SubaccountCreationResult> CreateSubaccountAsync(string email, string bankCode, string accountNumber, string businessName, decimal percentageCharge = 15m);
        Task<List<Bank>> GetBanksAsync();
    }

    public class Bank
    {
        public string Name { get; set; }
        public string Code { get; set; }
    }

    public class BankValidationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string AccountHolderName { get; set; }
    }

    public class SubaccountCreationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string SubaccountCode { get; set; }
        public string AccountHolderName { get; set; }
    }
}