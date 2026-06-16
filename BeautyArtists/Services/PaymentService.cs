using BeautyArtists.Data;
using BeautyArtists.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Text;

namespace BeautyArtists.Services
{
    public interface IPaymentService
    {
        Task<(bool success, string message, string authorizationUrl, string reference)> InitializePayment(string email, decimal amount, int bookingId);
        Task<(bool success, string message, PaystackVerifyData data)> VerifyPayment(string reference);
    }

    public class PaymentService : IPaymentService
    {
        private readonly IConfiguration _config;
        private readonly ApplicationDbContext _context;
        private readonly HttpClient _httpClient;

        public PaymentService(IConfiguration config, ApplicationDbContext context, IHttpClientFactory httpClientFactory)
        {
            _config = config;
            _context = context;
            _httpClient = httpClientFactory.CreateClient();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config["Paystack:SecretKey"]);
        }

        public async Task<(bool success, string message, string authorizationUrl, string reference)> InitializePayment(string email, decimal amount, int bookingId)
        {
            try
            {
                int amountInCents = (int)(amount * 100);
                string reference = GenerateReference();

                var request = new PaystackInitRequest
                {
                    email = email,
                    amount = amountInCents,
                    currency = "ZAR",
                    reference = reference,
                    callback_url = _config["Paystack:CallbackUrl"]
                };

                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("https://api.paystack.co/transaction/initialize", content);
                var responseString = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<PaystackInitResponse>(responseString);

                if (result.status && result.data != null)
                {
                    // Save payment record
                    var payment = new Payment
                    {
                        BookingId = bookingId,
                        Email = email,
                        Amount = amount,
                        Reference = reference,
                        Status = "pending",
                        IsDeposit = true
                    };
                    _context.Payments.Add(payment);
                    await _context.SaveChangesAsync();

                    return (true, result.message, result.data.authorization_url, reference);
                }

                return (false, result.message, null, null);
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}", null, null);
            }
        }

        public async Task<(bool success, string message, PaystackVerifyData data)> VerifyPayment(string reference)
        {
            try
            {
                var response = await _httpClient.GetAsync($"https://api.paystack.co/transaction/verify/{reference}");
                var responseString = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<PaystackVerifyResponse>(responseString);

                if (result.status && result.data != null)
                {
                    // Update payment record
                    var payment = await _context.Payments.FirstOrDefaultAsync(p => p.Reference == reference);
                    if (payment != null)
                    {
                        payment.Status = result.data.status == "success" ? "success" : "failed";
                        payment.PaymentMethod = result.data.channel;
                        payment.PaidAt = result.data.paid_at != null ? DateTime.Parse(result.data.paid_at) : DateTime.UtcNow;
                        await _context.SaveChangesAsync();
                    }

                    return (result.status, result.message, result.data);
                }

                return (false, result.message, null);
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}", null);
            }
        }

        private string GenerateReference()
        {
            return $"BEAUTY_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}";
        }


    }
}