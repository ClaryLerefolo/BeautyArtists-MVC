using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using BeautyArtists.Models.ViewModels;

namespace BeautyArtists.Services
{
    public interface IPaystackService
    {
        Task<BankValidationResult> ValidateBankAccountAsync(string bankCode, string accountNumber);
        Task<SubaccountCreationResult> CreateSubaccountAsync(string email, string bankCode, string accountNumber, string businessName, decimal percentageCharge = 0m);
        Task<List<Bank>> GetBanksAsync();
        Task<PaymentInitResult> InitializePaymentAsync(string email, decimal amount, int bookingId, string? subaccountCode = null, decimal? platformFee = null);

        // ─── NEW: TRANSFER METHODS ───
        Task<TransferRecipientResult> CreateTransferRecipientAsync(string name, string accountNumber, string bankCode, string email);
        Task<TransferResult> InitiateTransferAsync(string recipientCode, int amountInCents, string reference, string reason);
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

    public class PaymentInitResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string AuthorizationUrl { get; set; }
    }

    // ─── NEW: TRANSFER RESULT DTOs ───
    public class TransferRecipientResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string RecipientCode { get; set; }
        public string AccountHolderName { get; set; }
    }

    public class TransferResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string TransferCode { get; set; }
        public string Reference { get; set; }
    }

    public class PaystackService : IPaystackService
    {
        private readonly HttpClient _httpClient;
        private readonly string _secretKey;
        private readonly bool _isTestMode;
        private readonly ILogger<PaystackService> _logger;
        private readonly string _currency;

        // ─── SOUTH AFRICAN BANK CODES ───
        private readonly List<Bank> _saBanks = new List<Bank>
        {
            new Bank { Name = "ABSA", Code = "632005" },
            new Bank { Name = "Capitec", Code = "470010" },
            new Bank { Name = "FNB", Code = "250655" },
            new Bank { Name = "Nedbank", Code = "198765" },
            new Bank { Name = "Standard Bank", Code = "051001" },
            new Bank { Name = "Bank Zero", Code = "679000" },
            new Bank { Name = "Discovery Bank", Code = "679000" },
            new Bank { Name = "TymeBank", Code = "678910" },
            new Bank { Name = "African Bank", Code = "430000" },
            new Bank { Name = "Investec", Code = "580105" }
        };

        public PaystackService(HttpClient httpClient, IConfiguration configuration, ILogger<PaystackService> logger)
        {
            _httpClient = httpClient;
            _secretKey = configuration["Paystack:SecretKey"] ?? throw new Exception("Paystack Secret Key is missing");
            _isTestMode = configuration["Paystack:Mode"]?.ToLower() == "test";
            _currency = configuration["Paystack:Currency"] ?? "ZAR";
            _logger = logger;
            _httpClient.BaseAddress = new Uri("https://api.paystack.co/");

            // ✅ LOG THE KEY (first 8 chars) TO CONFIRM IT'S LOADED
            Console.WriteLine($"🔑 SecretKey loaded: {_secretKey.Substring(0, Math.Min(_secretKey.Length, 8))}...");
            Console.WriteLine($"🔍 Paystack Mode: {(_isTestMode ? "TEST" : "LIVE")}");
            Console.WriteLine($"🔍 Currency: {_currency}");
        }

        // ─── HELPER TO SET AUTH HEADER ───
        private void SetAuthHeader()
        {
            if (_httpClient.DefaultRequestHeaders.Contains("Authorization"))
                _httpClient.DefaultRequestHeaders.Remove("Authorization");
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_secretKey.Trim()}");
        }

        public async Task<List<Bank>> GetBanksAsync()
        {
            try
            {
                SetAuthHeader(); // ✅ ADDED

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = await _httpClient.GetAsync("bank?country=south-africa", cts.Token);
                var json = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"🔍 Banks API Response: {json}");

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
            return _saBanks;
        }
        public async Task<BankValidationResult> ValidateBankAccountAsync(string bankCode, string accountNumber)
        {
            // ─── SKIP VALIDATION FOR ZAR ───
            if (_currency == "ZAR")
            {
                Console.WriteLine($"🔍 ZAR MODE: Skipping bank validation for {bankCode}/{accountNumber}");
                return new BankValidationResult
                {
                    Success = true,
                    Message = "ZAR bank account - validation skipped (verified on recipient creation)",
                    AccountHolderName = "" // We'll let the artist enter their name manually
                };
            }

            // ─── TEST MODE BYPASS ───
            if (_isTestMode)
            {
                return new BankValidationResult
                {
                    Success = true,
                    Message = "Test mode validation bypassed",
                    AccountHolderName = "Test Artist (Test Mode)"
                };
            }

            // ─── VALIDATE FOR OTHER CURRENCIES (NGN, USD, GHS, KES) ───
            try
            {
                SetAuthHeader(); // Ensure auth header is set

                var url = $"bank/resolve?account_number={accountNumber}&bank_code={bankCode}";
                Console.WriteLine($"🔍 Validating {url}");

                var response = await _httpClient.GetAsync(url);
                var json = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"🔍 Paystack Response: {json}");

                // ─── DESERIALIZE RESPONSE ───
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

        public async Task<SubaccountCreationResult> CreateSubaccountAsync(
            string email,
            string bankCode,
            string accountNumber,
            string businessName,
            decimal percentageCharge = 0m)
        {
            try
            {
                SetAuthHeader(); // ✅ ADDED

                var payload = new
                {
                    business_name = businessName,
                    bank_code = bankCode,
                    account_number = accountNumber,
                    percentage_charge = (float)percentageCharge,
                    description = $"Subaccount for {businessName} (Email: {email})",
                    currency = _currency,
                    active = true
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

        public async Task<PaymentInitResult> InitializePaymentAsync(string email, decimal amount, int bookingId, string? subaccountCode = null, decimal? platformFee = null)
        {
            try
            {
                SetAuthHeader(); // ✅ ADDED

                Console.WriteLine($"💰 [Paystack] Initializing payment for booking {bookingId}");
                Console.WriteLine($"💰 [Paystack] Amount: {amount}, Email: {email}");
                Console.WriteLine($"💰 [Paystack] Subaccount: {subaccountCode ?? "None"}");
                Console.WriteLine($"💰 [Paystack] Platform Fee: {platformFee ?? 0}");

                var callbackUrl = "https://rubior.co.za/Payment/PaymentCallback";

                var requestData = new Dictionary<string, object>
                {
                    ["email"] = email,
                    ["amount"] = (int)(amount * 100),
                    ["callback_url"] = callbackUrl,
                    ["metadata"] = new { booking_id = bookingId }
                };

                if (!string.IsNullOrEmpty(subaccountCode))
                {
                    requestData["subaccount"] = subaccountCode;

                    if (platformFee.HasValue && platformFee.Value > 0)
                    {
                        requestData["transaction_charge"] = (int)(platformFee.Value * 100);
                        Console.WriteLine($"💰 [Paystack] Transaction charge set to: {platformFee.Value}");
                    }
                }

                var content = new StringContent(
                    JsonSerializer.Serialize(requestData),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.PostAsync("transaction/initialize", content);
                var json = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"🔍 [Paystack] Response: {json}");

                var result = JsonSerializer.Deserialize<PaystackInitResponse>(json);

                if (result?.status == true && result.data?.authorization_url != null)
                {
                    return new PaymentInitResult
                    {
                        Success = true,
                        Message = "Payment initialized",
                        AuthorizationUrl = result.data.authorization_url
                    };
                }

                return new PaymentInitResult
                {
                    Success = false,
                    Message = result?.message ?? "Payment initialization failed"
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [Paystack] InitializePayment error: {ex.Message}");
                return new PaymentInitResult
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        // ─── NEW: CREATE TRANSFER RECIPIENT ───
        public async Task<TransferRecipientResult> CreateTransferRecipientAsync(
            string name,
            string accountNumber,
            string bankCode,
            string email)
        {
            try
            {
                SetAuthHeader(); // ✅ ADDED

                var payload = new
                {
                    type = "basa",
                    name = name,
                    account_number = accountNumber,
                    bank_code = bankCode,
                    currency = _currency,
                    email = email
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("transferrecipient", content);
                var json = await response.Content.ReadAsStringAsync();

                _logger.LogInformation($"Transfer recipient response: {json}");

                var result = JsonSerializer.Deserialize<PaystackTransferRecipientResponse>(json);

                if (result?.status == true && result.data != null)
                {
                    return new TransferRecipientResult
                    {
                        Success = true,
                        Message = "Recipient created successfully",
                        RecipientCode = result.data.recipient_code,
                        AccountHolderName = result.data.name
                    };
                }

                return new TransferRecipientResult
                {
                    Success = false,
                    Message = result?.message ?? "Failed to create transfer recipient"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"CreateTransferRecipient error: {ex.Message}");
                return new TransferRecipientResult
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        // ─── NEW: INITIATE TRANSFER ───
        public async Task<TransferResult> InitiateTransferAsync(
            string recipientCode,
            int amountInCents,
            string reference,
            string reason)
        {
            try
            {
                SetAuthHeader(); // ✅ ADDED

                var payload = new
                {
                    source = "balance",
                    amount = amountInCents,
                    recipient = recipientCode,
                    reason = reason,
                    reference = reference
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("transfer", content);
                var json = await response.Content.ReadAsStringAsync();

                _logger.LogInformation($"Transfer response: {json}");

                var result = JsonSerializer.Deserialize<PaystackTransferResponse>(json);

                if (result?.status == true && result.data != null)
                {
                    return new TransferResult
                    {
                        Success = true,
                        Message = "Transfer initiated successfully",
                        TransferCode = result.data.transfer_code,
                        Reference = result.data.reference
                    };
                }

                return new TransferResult
                {
                    Success = false,
                    Message = result?.message ?? "Failed to initiate transfer"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"InitiateTransfer error: {ex.Message}");
                return new TransferResult
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

    // ─── TRANSFER RECIPIENT RESPONSE ───
    public class PaystackTransferRecipientResponse
    {
        public bool status { get; set; }
        public string message { get; set; }
        public TransferRecipientData data { get; set; }
    }

    public class TransferRecipientData
    {
        public string recipient_code { get; set; }
        public string name { get; set; }
        public string account_number { get; set; }
        public string bank_code { get; set; }
        public string currency { get; set; }
        public string email { get; set; }
    }

    // ─── TRANSFER RESPONSE ───
    public class PaystackTransferResponse
    {
        public bool status { get; set; }
        public string message { get; set; }
        public TransferData data { get; set; }
    }

    public class TransferData
    {
        public string transfer_code { get; set; }
        public string reference { get; set; }
        public int amount { get; set; }
        public string currency { get; set; }
        public string status { get; set; }
        public string recipient { get; set; }
    }

    // ─── PaystackInitResponse and PaystackInitData are defined in PaymentService.cs ───
}