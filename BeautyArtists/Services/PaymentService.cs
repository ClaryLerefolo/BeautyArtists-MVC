using BeautyArtists.Data;
using BeautyArtists.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace BeautyArtists.Services
{
    public interface IPaymentService
    {
        Task<(bool success, string message, string authorizationUrl, string reference)> InitializePaymentAsync(
            string email,
            decimal amount,
            int bookingId,
            string subaccount = null,
            decimal platformFee = 0m);

        Task<(bool success, string message, PaystackVerifyData data)> VerifyPayment(string reference);
    }

    public class PaymentService : IPaymentService
    {
        private readonly IConfiguration _config;
        private readonly ApplicationDbContext _context;
        private readonly HttpClient _httpClient;
        private readonly string _secretKey;

        // ─── PRICING CONSTANTS ───
        private const decimal CLIENT_MARKUP_RATE = 0.04m;
        private const decimal BOOKING_FEE = 5.00m;
        private const decimal NEW_CLIENT_COMMISSION = 0.10m;
        private const decimal REPEAT_CLIENT_FLAT_FEE = 15.00m;
        private const decimal MIN_PLATFORM_FEE = 8.00m;

        public PaymentService(IConfiguration config, ApplicationDbContext context, IHttpClientFactory httpClientFactory)
        {
            _config = config;
            _context = context;
            _httpClient = httpClientFactory.CreateClient();
            _secretKey = _config["Paystack:SecretKey"] ?? throw new Exception("Paystack Secret Key is missing in PaymentService");

            // Log that the key is loaded
            Console.WriteLine($"🔑 PaymentService SecretKey loaded: {_secretKey.Substring(0, Math.Min(_secretKey.Length, 8))}...");
        }

        // ─── HELPER TO SET AUTH HEADER ───
        private void SetAuthHeader()
        {
            if (_httpClient.DefaultRequestHeaders.Contains("Authorization"))
                _httpClient.DefaultRequestHeaders.Remove("Authorization");
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_secretKey.Trim()}");
        }

        private decimal CalculateCardProcessingFee(decimal servicePrice)
        {
            return servicePrice * CLIENT_MARKUP_RATE;
        }

        private decimal CalculateClientTotal(decimal servicePrice)
        {
            return servicePrice + CalculateCardProcessingFee(servicePrice) + BOOKING_FEE;
        }

        private decimal CalculateDepositAmount(decimal servicePrice)
        {
            decimal halfService = servicePrice / 2;
            decimal cardFee = CalculateCardProcessingFee(servicePrice);
            return halfService + cardFee + BOOKING_FEE;
        }

        private decimal CalculateFinalAmount(decimal servicePrice)
        {
            return servicePrice / 2;
        }

        // ─── INITIALIZE PAYMENT ───
        public async Task<(bool success, string message, string authorizationUrl, string reference)> InitializePaymentAsync(
            string email,
            decimal amount,
            int bookingId,
            string subaccount = null,
            decimal platformFee = 0m)
        {
            try
            {
                SetAuthHeader(); // ✅ Set header before request

                int amountInCents = (int)(amount * 100);
                string reference = GenerateReference();

                var booking = await _context.Bookings
                    .Include(b => b.UserService)
                        .ThenInclude(us => us.Artist)
                            .ThenInclude(a => a.ArtistProfile)
                    .FirstOrDefaultAsync(b => b.Id == bookingId);

                if (booking == null)
                {
                    return (false, "Booking not found", null, null);
                }

                decimal servicePrice = booking.ServicePrice;
                decimal clientTotal = CalculateClientTotal(servicePrice);
                bool isDeposit = !booking.IsDepositPaid;
                bool isFullPayment = Math.Abs(amount - clientTotal) < 0.01m;

                Console.WriteLine($"📊 Booking {bookingId}:");
                Console.WriteLine($"   ServicePrice: R{servicePrice}");
                Console.WriteLine($"   Amount Paid: R{amount}");
                Console.WriteLine($"   Client Total: R{clientTotal}");
                Console.WriteLine($"   Platform Fee: R{platformFee}");
                Console.WriteLine($"   IsDeposit: {isDeposit}");
                Console.WriteLine($"   IsFullPayment: {isFullPayment}");

                var requestPayload = new
                {
                    email = email,
                    amount = amountInCents,
                    currency = "ZAR",
                    reference = reference,
                    callback_url = _config["Paystack:CallbackUrl"],
                    subaccount = !string.IsNullOrEmpty(subaccount) && !subaccount.StartsWith("TEST_SUBACCOUNT_") ? subaccount : null,
                    transaction_charge = platformFee > 0 ? (int)(platformFee * 100) : 0
                };
                var json = JsonConvert.SerializeObject(requestPayload, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                });
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("https://api.paystack.co/transaction/initialize", content);
                var responseString = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"Paystack Init Response: {responseString}");

                var result = JsonConvert.DeserializeObject<PaystackInitResponse>(responseString);

                if (result != null && result.status && result.data != null)
                {
                    var payment = new Payment
                    {
                        BookingId = bookingId,
                        Email = email,
                        Amount = amount,
                        Reference = reference,
                        Status = "pending",
                        IsDeposit = isDeposit,
                        IsFullPayment = isFullPayment,
                        PaymentMethod = "pending",
                        PhoneNumber = ""
                    };
                    _context.Payments.Add(payment);
                    await _context.SaveChangesAsync();

                    return (true, result.message, result.data.authorization_url, reference);
                }

                return (false, result?.message ?? "Unknown error", null, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"InitializePayment Exception: {ex.Message}");
                return (false, $"Error: {ex.Message}", null, null);
            }
        }

        // ─── BUILD SPLIT OBJECT (kept for reference, but not used in new flow) ───
        private object BuildSplitObject(Booking booking, decimal amount)
        {
            var artistSubaccount = booking.UserService?.Artist?.ArtistProfile?.SubaccountCode;

            if (string.IsNullOrEmpty(artistSubaccount) || artistSubaccount.StartsWith("TEST_SUBACCOUNT_"))
            {
                Console.WriteLine($"⚠️ No valid subaccount for artist {booking.UserService?.ArtistId}. Skipping split.");
                return null;
            }

            decimal servicePrice = booking.ServicePrice;
            decimal clientTotal = CalculateClientTotal(servicePrice);
            bool isDeposit = !booking.IsDepositPaid;
            bool isFullPayment = Math.Abs(amount - clientTotal) < 0.01m;

            decimal artistShare;
            decimal platformShare;

            if (isDeposit && !isFullPayment)
            {
                artistShare = servicePrice / 2;
                platformShare = amount - artistShare;
            }
            else
            {
                artistShare = servicePrice / 2;
                platformShare = amount - artistShare;
            }

            int artistShareInCents = (int)(artistShare * 100);

            Console.WriteLine($"💰 Split: Artist gets R{artistShare}, Platform gets R{platformShare}");

            return new
            {
                type = "flat",
                bearer_type = "account",
                subaccounts = new[]
                {
                    new
                    {
                        subaccount = artistSubaccount,
                        share = artistShareInCents
                    }
                }
            };
        }

        // ─── VERIFY PAYMENT ───
        public async Task<(bool success, string message, PaystackVerifyData data)> VerifyPayment(string reference)
        {
            try
            {
                SetAuthHeader(); // ✅ Set header before request

                var response = await _httpClient.GetAsync($"https://api.paystack.co/transaction/verify/{reference}");
                var responseString = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"Paystack Verify Response: {responseString}");

                var result = JsonConvert.DeserializeObject<PaystackVerifyResponse>(responseString);

                if (result != null && result.status && result.data != null)
                {
                    var payment = await _context.Payments.FirstOrDefaultAsync(p => p.Reference == reference);
                    if (payment != null)
                    {
                        payment.Status = result.data.status == "success" ? "success" : "failed";
                        payment.PaymentMethod = result.data.channel;
                        payment.PaidAt = result.data.paid_at != null ? DateTime.Parse(result.data.paid_at) : DateTime.UtcNow;
                        await _context.SaveChangesAsync();
                    }

                    return (true, result.message, result.data);
                }

                return (false, result?.message ?? "Unknown error", null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"VerifyPayment Exception: {ex.Message}");
                return (false, $"Error: {ex.Message}", null);
            }
        }

        private string GenerateReference()
        {
            return $"BEAUTY_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}";
        }
    }

    // ─── RESPONSE MODELS ───
    public class PaystackInitResponse
    {
        public bool status { get; set; }
        public string message { get; set; }
        public PaystackInitData data { get; set; }
    }

    public class PaystackInitData
    {
        public string authorization_url { get; set; }
        public string access_code { get; set; }
        public string reference { get; set; }
    }

    public class PaystackVerifyResponse
    {
        public bool status { get; set; }
        public string message { get; set; }
        public PaystackVerifyData data { get; set; }
    }

    public class PaystackVerifyData
    {
        public int amount { get; set; }
        public string currency { get; set; }
        public string status { get; set; }
        public string reference { get; set; }
        public string channel { get; set; }
        public string paid_at { get; set; }
        public PaystackCustomer customer { get; set; }
        public PaystackAuthorization authorization { get; set; }
    }

    public class PaystackCustomer
    {
        public int id { get; set; }
        public string first_name { get; set; }
        public string last_name { get; set; }
        public string email { get; set; }
    }

    public class PaystackAuthorization
    {
        public string authorization_code { get; set; }
        public string card_type { get; set; }
        public string last4 { get; set; }
        public string exp_month { get; set; }
        public string exp_year { get; set; }
        public string bank { get; set; }
        public string channel { get; set; }
    }
}