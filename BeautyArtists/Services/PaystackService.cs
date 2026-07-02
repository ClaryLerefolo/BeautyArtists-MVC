using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using BeautyArtists.Models.ViewModels;

namespace BeautyArtists.Services
{
    public class PaystackService : IPaystackService
    {
        private readonly HttpClient _httpClient;
        private readonly string _secretKey;
        private readonly bool _isTestMode;
        private readonly ILogger<PaystackService> _logger;

        public PaystackService(HttpClient httpClient, IConfiguration configuration, ILogger<PaystackService> logger)
        {
            _httpClient = httpClient;
            _secretKey = configuration["Paystack:SecretKey"] ?? throw new Exception("Paystack Secret Key is missing");
            _isTestMode = configuration["Paystack:Mode"]?.ToLower() != "live"; // Default to test
            _logger = logger;
            _httpClient.BaseAddress = new Uri("https://api.paystack.co/");
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_secretKey}");
        }

        // ─── GET BANKS ───
        public async Task<List<Bank>> GetBanksAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("bank?country=south-africa");
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<PaystackBankResponse>(json);

                if (result?.status == true && result.data != null)
                {
                    return result.data.Select(b => new Bank
                    {
                        Name = b.name,
                        Code = b.code
                    }).ToList();
                }

                return new List<Bank>();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Get banks error: {ex.Message}");
                return new List<Bank>();
            }
        }

        // ─── VALIDATE BANK ACCOUNT (R3 FEE) ───
        public async Task<BankValidationResult> ValidateBankAccountAsync(string bankCode, string accountNumber)
        {
            try
            {
                var url = $"bank/resolve?account_number={accountNumber}&bank_code={bankCode}";
                var response = await _httpClient.GetAsync(url);
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<PaystackApiResponse>(json);

                _logger.LogInformation($"Bank validation response: {json}");

                if (result?.status == true && result.data != null)
                {
                    return new BankValidationResult
                    {
                        Success = true,
                        Message = "Account validated successfully",
                        AccountHolderName = result.data.account_name ?? ""
                    };
                }

                return new BankValidationResult
                {
                    Success = false,
                    Message = result?.message ?? "Account validation failed"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Bank validation error: {ex.Message}");
                return new BankValidationResult
                {
                    Success = false,
                    Message = $"Validation error: {ex.Message}"
                };
            }
        }

        // ─── CREATE SUBACCOUNT ───
        public async Task<SubaccountCreationResult> CreateSubaccountAsync(
            string email,
            string bankCode,
            string accountNumber,
            string businessName,
            decimal percentageCharge = 15m)
        {
            try
            {
                var payload = new
                {
                    business_name = businessName,
                    bank_code = bankCode,
                    account_number = accountNumber,
                    percentage_charge = (float)percentageCharge,
                    description = $"Subaccount for {businessName} (Email: {email})"
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("subaccount", content);
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<PaystackApiResponse>(json);

                _logger.LogInformation($"Subaccount creation response: {json}");

                if (result?.status == true && result.data != null)
                {
                    return new SubaccountCreationResult
                    {
                        Success = true,
                        Message = "Subaccount created successfully",
                        SubaccountCode = result.data.subaccount_code ?? "",
                        AccountHolderName = result.data.account_name ?? ""
                    };
                }

                return new SubaccountCreationResult
                {
                    Success = false,
                    Message = result?.message ?? "Failed to create subaccount"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Subaccount creation error: {ex.Message}");
                return new SubaccountCreationResult
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }
    }

    // ─── PAYSTACK API RESPONSE MODELS ───
    public class PaystackApiResponse
    {
        public bool status { get; set; }
        public string message { get; set; }
        public PaystackData data { get; set; }
    }

    public class PaystackData
    {
        public string account_name { get; set; }
        public string subaccount_code { get; set; }
        public string bank_code { get; set; }
        public string account_number { get; set; }
        public string business_name { get; set; }
        public string description { get; set; }
        public bool active { get; set; }
    }

    public class PaystackBankResponse
    {
        public bool status { get; set; }
        public string message { get; set; }
        public List<PaystackBank> data { get; set; }
    }

    public class PaystackBank
    {
        public string name { get; set; }
        public string code { get; set; }
        public string longcode { get; set; }
        public string gateway { get; set; }
        public bool pay_with_bank { get; set; }
        public bool active { get; set; }
        public string country { get; set; }
        public string currency { get; set; }
        public string type { get; set; }
        public bool is_deleted { get; set; }
    }
}