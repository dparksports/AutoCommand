using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AutoCommand.Services
{
    public class AnalyticsService
    {
        private static AnalyticsService _instance;
        public static AnalyticsService Instance => _instance ??= new AnalyticsService();

        // TODO: Replace with actual GA4/Firebase Measurement ID and API Secret
        private const string MeasurementId = "G-XXXXXXXXXX";
        private const string ApiSecret = "YOUR_API_SECRET_HERE";
        private const string Endpoint = $"https://www.google-analytics.com/mp/collect?measurement_id={MeasurementId}&api_secret={ApiSecret}";

        private const string RegKeyPath = @"Software\AutoCommand";
        private readonly HttpClient _httpClient;
        private string _clientId;

        public AnalyticsService()
        {
            _httpClient = new HttpClient();
            EnsureClientId();
        }

        public bool IsAnalyticsEnabled
        {
            get
            {
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(RegKeyPath);
                    var val = key?.GetValue("EnableAnalytics");
                    // Default to true unless explicitly turned off
                    return val == null || Convert.ToInt32(val) == 1;
                }
                catch
                {
                    return false;
                }
            }
            set
            {
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(RegKeyPath);
                    key?.SetValue("EnableAnalytics", value ? 1 : 0);
                }
                catch { }
            }
        }

        private void EnsureClientId()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegKeyPath);
                _clientId = key?.GetValue("ClientId") as string;
                if (string.IsNullOrEmpty(_clientId))
                {
                    _clientId = Guid.NewGuid().ToString();
                    key?.SetValue("ClientId", _clientId);
                }
            }
            catch
            {
                _clientId = Guid.NewGuid().ToString();
            }
        }

        public async Task TrackEventAsync(string eventName, Dictionary<string, object> parameters = null)
        {
            if (!IsAnalyticsEnabled) return;
            
            // Prevent actual network calls if the placeholder is still present
            if (MeasurementId == "G-XXXXXXXXXX" || ApiSecret == "YOUR_API_SECRET_HERE") return;

            try
            {
                var payload = new
                {
                    client_id = _clientId,
                    events = new[]
                    {
                        new
                        {
                            name = eventName,
                            @params = parameters ?? new Dictionary<string, object>()
                        }
                    }
                };

                string json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Fire and forget; do not await the result so it doesn't block UI or throw exceptions up
                _ = _httpClient.PostAsync(Endpoint, content);
            }
            catch 
            { 
                /* Fail silently for telemetry */ 
            }
        }
    }
}
