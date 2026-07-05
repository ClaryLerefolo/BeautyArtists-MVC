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
            _isTestMode = configuration["Paystack:Mode"]?.ToLower() != "live";
            _logger = logger;
            _httpClient.BaseAddress = new Uri("https://api.paystack.co/");
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_secretKey}");

            Console.WriteLine($"🔍 Paystack Mode: {(_isTestMode ? "TEST" : "LIVE")}");
        }

        // ─── GET BANKS ───
        public async Task<List<Bank>> GetBanksAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = await _httpClient.GetAsync("bank?country=south-africa", cts.Token);
                var json = await response.Content.ReadAsStringAsync();

                var result = JsonSerializer.Deserialize<PaystackBankResponse>(json);

                if (result?.status == true && result.data != null && result.data.Any())
                {
                    return result.data.Select(b => new Bank
                    {
                        Name = b.name,
                        Code = b.code
                    }).ToList();
                }

                _logger.LogWarning("Banks API returned no data, using fallback list.");
                return GetFallbackBanks();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Get banks error: {ex.Message}");
                return GetFallbackBanks();
            }
        }

        private List<Bank> GetFallbackBanks()
        {
            if (_isTestMode)
            {
                return new List<Bank>
                {
                    new Bank { Name = "ABSA (Test)", Code = "000003" },
                    new Bank { Name = "Capitec (Test)", Code = "000002" },
                    new Bank { Name = "FNB (Test)", Code = "000001" },
                    new Bank { Name = "Standard Bank (Test)", Code = "000004" }
                };
            }

            return new List<Bank>
            {
                new Bank { Name = "ABSA", Code = "585001" },
                new Bank { Name = "Capitec", Code = "585010" },
                new Bank { Name = "FNB", Code = "585012" },
                new Bank { Name = "Nedbank", Code = "585013" },
                new Bank { Name = "Standard Bank", Code = "585014" }
            };
        }

        // ─── VALIDATE BANK ACCOUNT ───
        public async Task<BankValidationResult> ValidateBankAccountAsync(string bankCode, string accountNumber)
        {
            // ─── 🔥 TEST MODE: ALWAYS SUCCEED ───
            if (_isTestMode)
            {
                Console.WriteLine($"🔍 TEST MODE: Bypassing validation for {bankCode}/{accountNumber}");
                return new BankValidationResult
                {
                    Success = true,
                    Message = "Test mode validation bypassed",
                    AccountHolderName = "Test Artist (Test Mode)"
                };
            }

            // ─── LIVE MODE: REAL VALIDATION ───
            try
            {
                var url = $"bank/resolve?account_number={accountNumber}&bank_code={bankCode}";
                Console.WriteLine($"🔍 LIVE MODE: Validating {url}");

                var response = await _httpClient.GetAsync(url);
                var json = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"🔍 Paystack Response: {json}");

                var result = JsonSerializer.Deserialize<PaystackApiResponse>(json);

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
                Console.WriteLine($"🔍 Subaccount Response: {json}");

                var result = JsonSerializer.Deserialize<PaystackApiResponse>(json);

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