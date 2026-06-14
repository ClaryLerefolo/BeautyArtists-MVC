using System.Text.Json.Serialization;

namespace BeautyArtists.Models
{
    // Request to initialize payment
    public class PaystackInitRequest
    {
        public string email { get; set; }
        public int amount { get; set; }  // in cents
        public string currency { get; set; } = "ZAR";
        public string reference { get; set; }
        public string callback_url { get; set; }
    }

    // Response from initialization
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

    // Response from verification
    public class PaystackVerifyResponse
    {
        public bool status { get; set; }
        public string message { get; set; }
        public PaystackVerifyData data { get; set; }
    }

    public class PaystackVerifyData
    {
        public string reference { get; set; }
        public int amount { get; set; }
        public string status { get; set; }  // "success", "failed"
        public string channel { get; set; }
        public string gateway_response { get; set; }
        public string paid_at { get; set; }
    }
}