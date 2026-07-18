using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TestFXTrade.Fx.Domain;
using UnityEngine;
using UnityEngine.Networking;

namespace TestFXTrade.Fx.MarketData
{
    public sealed class AzureRelayMarketDataProvider : IFxMarketDataProvider
    {
        private readonly string relayBaseUrl;

        public AzureRelayMarketDataProvider(string relayBaseUrl)
        {
            this.relayBaseUrl = AzureRelaySettings.NormalizeBaseUrl(relayBaseUrl);
        }

        public string ProviderName => "Twelve Data";

        public async Task<MarketQuote> GetLatestQuoteAsync(string symbol, CancellationToken cancellationToken)
        {
            RequireRelayUrl();
            string url = $"{relayBaseUrl}/api/market/quote?symbol={UnityWebRequest.EscapeURL(symbol)}";
            string json = await GetTextAsync(url, cancellationToken);
            AzureRelayQuoteResponse response = JsonUtility.FromJson<AzureRelayQuoteResponse>(json);

            if (response == null || response.price <= 0d)
            {
                throw new InvalidOperationException("行情服务未返回有效报价。");
            }

            DateTime timeUtc = ParseDateTime(response.timeUtc, DateTime.UtcNow);
            string responseSymbol = string.IsNullOrWhiteSpace(response.symbol) ? symbol : response.symbol;
            string provider = string.IsNullOrWhiteSpace(response.provider) ? ProviderName : response.provider;
            return new MarketQuote(responseSymbol, response.price, timeUtc, response.isTimestampReliable, provider);
        }

        public async Task<IReadOnlyList<Candle>> GetCandlesAsync(
            string symbol,
            string interval,
            int outputSize,
            CancellationToken cancellationToken)
        {
            RequireRelayUrl();
            outputSize = Mathf.Clamp(outputSize, 30, 500);
            string url = $"{relayBaseUrl}/api/market/candles?symbol={UnityWebRequest.EscapeURL(symbol)}" +
                         $"&interval={UnityWebRequest.EscapeURL(interval)}&outputSize={outputSize}";
            string json = await GetTextAsync(url, cancellationToken);
            AzureRelayCandlesResponse response = JsonUtility.FromJson<AzureRelayCandlesResponse>(json);

            if (response?.candles == null || response.candles.Length == 0)
            {
                throw new InvalidOperationException("行情服务未返回K线数据。");
            }

            List<Candle> candles = new List<Candle>(response.candles.Length);
            for (int i = 0; i < response.candles.Length; i++)
            {
                AzureRelayCandle row = response.candles[i];
                if (row == null || row.open <= 0d || row.high <= 0d || row.low <= 0d || row.close <= 0d)
                {
                    throw new InvalidOperationException("行情服务返回了无效K线。");
                }

                candles.Add(new Candle(
                    ParseDateTime(row.timeUtc, DateTime.UtcNow),
                    row.open,
                    row.high,
                    row.low,
                    row.close));
            }

            return candles;
        }

        private void RequireRelayUrl()
        {
            if (string.IsNullOrWhiteSpace(relayBaseUrl))
            {
                throw new InvalidOperationException("行情服务暂不可用，请稍后重试。");
            }
        }

        private static async Task<string> GetTextAsync(string url, CancellationToken cancellationToken)
        {
            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.timeout = 25;
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            string responseJson = request.downloadHandler.text;
            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new InvalidOperationException(ReadRelayError(responseJson, request.error));
            }

            return responseJson;
        }

        private static string ReadRelayError(string responseJson, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(responseJson))
            {
                try
                {
                    AzureRelayErrorResponse response = JsonUtility.FromJson<AzureRelayErrorResponse>(responseJson);
                    if (!string.IsNullOrWhiteSpace(response?.error))
                    {
                        return $"行情服务请求失败：{response.error}";
                    }
                }
                catch (Exception)
                {
                }
            }

            return $"行情服务请求失败：{fallback}";
        }

        private static DateTime ParseDateTime(string value, DateTime fallback)
        {
            if (DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTime parsed))
            {
                return parsed;
            }

            return fallback;
        }

        [Serializable]
        private sealed class AzureRelayQuoteResponse
        {
            public string symbol;
            public double price;
            public string timeUtc;
            public bool isTimestampReliable;
            public string provider;
        }

        [Serializable]
        private sealed class AzureRelayCandlesResponse
        {
            public AzureRelayCandle[] candles;
        }

        [Serializable]
        private sealed class AzureRelayCandle
        {
            public string timeUtc;
            public double open;
            public double high;
            public double low;
            public double close;
        }

        [Serializable]
        private sealed class AzureRelayErrorResponse
        {
            public string error;
        }
    }
}
