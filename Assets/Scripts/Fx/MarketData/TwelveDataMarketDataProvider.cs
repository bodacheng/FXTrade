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
    public sealed class TwelveDataMarketDataProvider : IFxMarketDataProvider
    {
        private const string BaseUrl = "https://api.twelvedata.com";
        private readonly string apiKey;

        public string ProviderName => "Twelve Data";

        public TwelveDataMarketDataProvider(string apiKey)
        {
            this.apiKey = apiKey ?? string.Empty;
        }

        public async Task<MarketQuote> GetLatestQuoteAsync(string symbol, CancellationToken cancellationToken)
        {
            RequireApiKey();

            string url = $"{BaseUrl}/quote?symbol={UnityWebRequest.EscapeURL(symbol)}&apikey={UnityWebRequest.EscapeURL(apiKey)}";
            string json = await GetTextAsync(url, cancellationToken);
            TwelveDataQuoteResponse response = JsonUtility.FromJson<TwelveDataQuoteResponse>(json);

            if (response == null)
            {
                throw new InvalidOperationException("报价响应为空。");
            }

            ThrowIfApiError(response.status, response.message);

            double price = ParseRequiredDouble(response.close, "close");
            DateTime timeUtc = DateTime.UtcNow;
            bool reliableTime = false;

            if (response.timestamp > 0)
            {
                timeUtc = DateTimeOffset.FromUnixTimeSeconds(response.timestamp).UtcDateTime;
                reliableTime = true;
            }
            else if (!string.IsNullOrWhiteSpace(response.datetime) && DateTime.TryParse(response.datetime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTime parsed))
            {
                timeUtc = parsed.ToUniversalTime();
                reliableTime = true;
            }

            return new MarketQuote(symbol, price, timeUtc, reliableTime, ProviderName);
        }

        public async Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, string interval, int outputSize, CancellationToken cancellationToken)
        {
            RequireApiKey();

            outputSize = Mathf.Clamp(outputSize, 30, 500);
            string url = $"{BaseUrl}/time_series?symbol={UnityWebRequest.EscapeURL(symbol)}&interval={UnityWebRequest.EscapeURL(interval)}&outputsize={outputSize}&apikey={UnityWebRequest.EscapeURL(apiKey)}";
            string json = await GetTextAsync(url, cancellationToken);
            TwelveDataTimeSeriesResponse response = JsonUtility.FromJson<TwelveDataTimeSeriesResponse>(json);

            if (response == null)
            {
                throw new InvalidOperationException("K线响应为空。");
            }

            ThrowIfApiError(response.status, response.message);

            if (response.values == null || response.values.Length == 0)
            {
                throw new InvalidOperationException("数据源未返回K线数据。");
            }

            List<Candle> candles = new List<Candle>(response.values.Length);
            for (int i = response.values.Length - 1; i >= 0; i--)
            {
                TwelveDataCandle row = response.values[i];
                candles.Add(new Candle(
                    ParseDateTime(row.datetime),
                    ParseRequiredDouble(row.open, "open"),
                    ParseRequiredDouble(row.high, "high"),
                    ParseRequiredDouble(row.low, "low"),
                    ParseRequiredDouble(row.close, "close")));
            }

            return candles;
        }

        private void RequireApiKey()
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("请先在本地 .env 中配置 TWELVE_DATA_API_KEY。");
            }
        }

        private static async Task<string> GetTextAsync(string url, CancellationToken cancellationToken)
        {
            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.timeout = 20;
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new InvalidOperationException($"网络请求失败：{request.error}");
            }

            return request.downloadHandler.text;
        }

        private static void ThrowIfApiError(string status, string message)
        {
            if (string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
            {
                string detail = string.IsNullOrWhiteSpace(message) ? "未提供详细信息" : message;
                throw new InvalidOperationException($"Twelve Data API 错误：{detail}");
            }
        }

        private static double ParseRequiredDouble(string value, string fieldName)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                throw new InvalidOperationException($"无法解析 {fieldName} 的值“{value}”。");
            }

            return parsed;
        }

        private static DateTime ParseDateTime(string value)
        {
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTime parsed))
            {
                return parsed.ToUniversalTime();
            }

            return DateTime.UtcNow;
        }

        [Serializable]
        private sealed class TwelveDataQuoteResponse
        {
            public string symbol;
            public string datetime;
            public long timestamp;
            public string close;
            public string status;
            public string message;
        }

        [Serializable]
        private sealed class TwelveDataTimeSeriesResponse
        {
            public TwelveDataCandle[] values;
            public string status;
            public string message;
        }

        [Serializable]
        private sealed class TwelveDataCandle
        {
            public string datetime;
            public string open;
            public string high;
            public string low;
            public string close;
        }
    }
}
