using System;
using UnityEngine;

namespace TestFXTrade.Fx.MarketData
{
    public sealed class AzureRelaySettings
    {
        public const string BaseUrlVariableName = "AZURE_RELAY_BASE_URL";
        private const string ResourceName = "AzureRelayConfig";

        public AzureRelaySettings(string baseUrl, string sourceLabel)
        {
            BaseUrl = NormalizeBaseUrl(baseUrl);
            SourceLabel = sourceLabel ?? string.Empty;
        }

        public string BaseUrl { get; }

        public string SourceLabel { get; }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);

        public static AzureRelaySettings Load()
        {
            string environmentValue = Environment.GetEnvironmentVariable(BaseUrlVariableName);
            string normalizedEnvironmentValue = NormalizeBaseUrl(environmentValue);
            if (!string.IsNullOrWhiteSpace(normalizedEnvironmentValue))
            {
                return new AzureRelaySettings(normalizedEnvironmentValue, "environment variable");
            }

            TextAsset configAsset = Resources.Load<TextAsset>(ResourceName);
            if (configAsset == null || string.IsNullOrWhiteSpace(configAsset.text))
            {
                return new AzureRelaySettings(string.Empty, "Resources/AzureRelayConfig.json");
            }

            try
            {
                AzureRelayConfig config = JsonUtility.FromJson<AzureRelayConfig>(configAsset.text);
                return new AzureRelaySettings(config?.baseUrl, "Resources/AzureRelayConfig.json");
            }
            catch (Exception)
            {
                return new AzureRelaySettings(string.Empty, "Resources/AzureRelayConfig.json");
            }
        }

        public static string NormalizeBaseUrl(string value)
        {
            string candidate = (value ?? string.Empty).Trim().TrimEnd('/');
            if (candidate.Length == 0 ||
                candidate.IndexOf("YOUR_FUNCTION_APP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                !Uri.TryCreate(candidate, UriKind.Absolute, out Uri uri))
            {
                return string.Empty;
            }

            bool isHttps = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            bool isLoopbackHttp = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback;
            return isHttps || isLoopbackHttp ? candidate : string.Empty;
        }

        [Serializable]
        private sealed class AzureRelayConfig
        {
            public string baseUrl;
        }
    }
}
