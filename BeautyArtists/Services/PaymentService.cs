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
        Task<(bool success, string message, string authorizationUrl, string reference)> InitializePayment(string email, decimal amount, int bookingId, string subaccount = null);
        Task<(bool success, string message, PaystackVerifyData data)> VerifyPayment(string reference);
    }

    public class PaymentService : IPaymentService
    {
        private readonly IConfiguration _config;
        private readonly ApplicationDbContext _context;
        private readonly HttpClient _httpClient;
        private const decimal COMMISSION_RATE = 0.15m;
        private const decimal BOOKING_FEE = 5.00m;

        public PaymentService(IConfiguration config, ApplicationDbContext context, IHttpClientFactory httpClientFactory)
        {
            _config = config;
            _context = context;
            _httpClient = httpClientFactory.CreateClient();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config["Paystack:SecretKey"]);
        }

        // ─── HELPERS ───
        private decimal ClientTotal(Booking b) => (b.ServicePrice * (1 + COMMISSION_RATE)) + b.BookingFee;
        private decimal DepositAmount(Booking b) => (b.ServicePrice / 2) + b.BookingFee;
        private decimal FinalAmount(Booking b) => b.ServicePrice / 2;

        public async Task<(bool success, string message, string authorizationUrl, string reference)> InitializePayment(
            string email,
            decimal amount,
            int bookingId,
            string subaccount = null)
        {
            try
            {
                int amountInCents = (int)(amount * 100);
                string reference = GenerateReference();

                // ─── FETCH BOOKING WITH ARTIST DETAILS ───
                var booking = await _context.Bookings
                    .Include(b => b.UserService)
                        .ThenInclude(us => us.Artist)
                            .ThenInclude(a => a.ArtistProfile)
                    .FirstOrDefaultAsync(b => b.Id == bookingId);

                if (booking == null)
                {
                    return (false, "Booking not found", null, null);
                }

                // ─── DETERMINE PAYMENT TYPE ───
                decimal depositAmount = DepositAmount(booking);
                decimal clientTotal = ClientTotal(booking);
                bool isDeposit = !booking.IsDepositPaid;
                bool isFullPayment = Math.Abs(amount - clientTotal) < 0.01m;

                Console.WriteLine($"📊 Booking {bookingId}:");
                Console.WriteLine($"   ServicePrice: R{booking.ServicePrice}");
                Console.WriteLine($"   Amount Paid: R{amount}");
                Console.WriteLine($"   IsDeposit: {isDeposit}");
                Console.WriteLine($"   IsFullPayment: {isFullPayment}");

                // ─── BUILD REQUEST PAYLOAD ───
                var requestPayload = new
                {
                    email = email,
                    amount = amountInCents,
                    currency = "ZAR",
                    reference = reference,
                    callback_url = _config["Paystack:CallbackUrl"],
                    // ─── 🔥 SPLIT PAYMENT CONFIG ───
                    split = BuildSplitObject(booking, amount)
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
                    // ─── SAVE PAYMENT ───
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

        // ─── 🔥 BUILD SPLIT OBJECT ───
        private object BuildSplitObject(Booking booking, decimal amount)
        {
            // Get the artist's subaccount code
            var artistSubaccount = booking.UserService?.Artist?.ArtistProfile?.SubaccountCode;

            // If no subaccount, return null (no split)
            if (string.IsNullOrEmpty(artistSubaccount) || artistSubaccount.StartsWith("TEST_SUBACCOUNT_"))
            {
                Console.WriteLine($"⚠️ No valid subaccount for artist {booking.UserService?.ArtistId}. Skipping split.");
                return null;
            }

            // Calculate artist's share (100% of their service price)
            // For deposit: artist gets 50% of their price
            // For final/full: artist gets 100% of their price
            decimal artistShare;
            bool isDeposit = !booking.IsDepositPaid;
            bool isFullPayment = Math.Abs(amount - ClientTotal(booking)) < 0.01m;

            if (isDeposit && !isFullPayment)
            {
                // Deposit = 50% of artist price
                artistShare = booking.ServicePrice / 2;
            }
            else
            {
                // Final or full payment = 100% of artist price
                artistShare = booking.ServicePrice;
            }

            // Convert to cents
            int artistShareInCents = (int)(artistShare * 100);

            Console.WriteLine($"💰 Split: Artist subaccount {artistSubaccount} gets R{artistShare}");

            return new
            {
                type = "flat",
                bearer_type = "account", // Platform pays the fee
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

        public async Task<(bool success, string message, PaystackVerifyData data)> VerifyPayment(string reference)
        {
            try
            {
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

    // ─── REQUEST MODELS ───
    public class PaystackInitRequest
    {
        public string email { get; set; }
        public int amount { get; set; }
        public string currency { get; set; }
        public string reference { get; set; }
        public string callback_url { get; set; }
        public string subaccount { get; set; }
        public int transaction_charge { get; set; }
    }

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