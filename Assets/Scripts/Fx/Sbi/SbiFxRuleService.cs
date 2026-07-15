using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace TestFXTrade.Fx.Sbi
{
    public sealed class SbiFxRuleService
    {
        public const string OfficialRulesUrl =
            "https://www.sbisec.co.jp/ETGate/WPLETmgR001Control?OutSide=on&burl=search_fx&cat1=fx&cat2=service&dir=info&file=fx_hosyoukin.html&getFlg=on";

        private const string CacheFileName = "sbi_fx_rules.json";

        public string CachePath => Path.Combine(Application.persistentDataPath, CacheFileName);

        public SbiFxRuleSnapshot LoadLocal()
        {
            if (!File.Exists(CachePath))
            {
                return null;
            }

            try
            {
                SbiFxRuleSnapshot snapshot = JsonUtility.FromJson<SbiFxRuleSnapshot>(File.ReadAllText(CachePath));
                return snapshot != null && snapshot.IsUsable ? snapshot : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public async Task<SbiFxRuleSnapshot> RefreshAsync(CancellationToken cancellationToken)
        {
            using UnityWebRequest request = UnityWebRequest.Get(OfficialRulesUrl);
            request.timeout = 20;
            request.SetRequestHeader("Accept-Language", "ja-JP");
            request.SetRequestHeader("User-Agent", "TestFXTrade/1.0");

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new InvalidOperationException($"SBI FX规则读取失败：{request.error}");
            }

            string html = DecodeOfficialPage(request.downloadHandler.data, request.downloadHandler.text);
            SbiFxRuleSnapshot snapshot = ParseOfficialPage(html, DateTime.UtcNow);
            File.WriteAllText(CachePath, JsonUtility.ToJson(snapshot, true), Encoding.UTF8);
            return snapshot;
        }

        public static SbiFxRuleSnapshot ParseOfficialPage(string html, DateTime fetchedAtUtc)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                throw new InvalidOperationException("SBI FX规则页面为空。");
            }

            Match dateMatch = Regex.Match(html, @"\((\d{4}/\d{1,2}/\d{1,2})\s*適用分", RegexOptions.Singleline);
            string usdJpyRow = GetUsdJpyRowSlice(html);
            Match marginMatch = Regex.Match(
                usdJpyRow,
                "data-label=[\"']レバレッジ25倍[\"'][^>]*>\\s*([\\d,]+)",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            string plainText = WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", " "));
            plainText = Regex.Replace(plainText, @"\s+", " ");
            Match rateMatch = Regex.Match(
                plainText,
                @"レバレッジ25倍コース\s*[:：]\s*([\d.]+)\s*[％%]",
                RegexOptions.Singleline);
            Match minimumMatch = Regex.Match(
                plainText,
                @"SBI\s*FX\)?\s*は\s*([\d,]+)\s*通貨から",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            if (!dateMatch.Success || !marginMatch.Success || !rateMatch.Success || !minimumMatch.Success)
            {
                throw new InvalidOperationException("SBI FX官方页面格式已变化，无法安全解析最新规则。");
            }

            SbiFxRuleSnapshot snapshot = new SbiFxRuleSnapshot
            {
                SourceUrl = OfficialRulesUrl,
                FetchedAtUtc = fetchedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                ApplicableDate = dateMatch.Groups[1].Value,
                Pair = "USD/JPY",
                Leverage = 25,
                MarginRatePercent = ParseDouble(rateMatch.Groups[1].Value, "保证金率"),
                RequiredMarginPer10000Jpy = ParseInteger(marginMatch.Groups[1].Value, "必要保证金"),
                MinimumOrderUnits = ParseInteger(minimumMatch.Groups[1].Value, "最小交易单位")
            };

            if (!snapshot.IsUsable)
            {
                throw new InvalidOperationException("SBI FX规则内容不完整，未写入本地缓存。");
            }

            return snapshot;
        }

        private static string GetUsdJpyRowSlice(string html)
        {
            int pairIndex = html.IndexOf("米ドル/日本円", StringComparison.Ordinal);
            if (pairIndex < 0)
            {
                throw new InvalidOperationException("SBI FX官方页面中未找到 USD/JPY 保证金行。");
            }

            int length = Math.Min(12000, html.Length - pairIndex);
            return html.Substring(pairIndex, length);
        }

        private static string DecodeOfficialPage(byte[] data, string fallback)
        {
            if (data == null || data.Length == 0)
            {
                return fallback ?? string.Empty;
            }

            try
            {
                return Encoding.GetEncoding(932).GetString(data);
            }
            catch (Exception)
            {
                return string.IsNullOrWhiteSpace(fallback) ? Encoding.UTF8.GetString(data) : fallback;
            }
        }

        private static int ParseInteger(string value, string fieldName)
        {
            string normalized = value.Replace(",", string.Empty);
            if (!int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                throw new InvalidOperationException($"无法解析SBI FX的{fieldName}：{value}");
            }

            return parsed;
        }

        private static double ParseDouble(string value, string fieldName)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                throw new InvalidOperationException($"无法解析SBI FX的{fieldName}：{value}");
            }

            return parsed;
        }
    }
}
