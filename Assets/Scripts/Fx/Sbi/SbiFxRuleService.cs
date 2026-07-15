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
        private static readonly UTF8Encoding CacheEncoding = new UTF8Encoding(false);

        // SBI serves the rules page as Windows-31J; iOS IL2CPP may not include code-page tables.
        private static readonly byte[] Windows31JApplicableDateMarker = { 0x93, 0x4B, 0x97, 0x70, 0x95, 0xAA };
        private static readonly byte[] Windows31JUsdJpyMarker = { 0x95, 0xC4, 0x83, 0x68, 0x83, 0x8B, 0x2F, 0x93, 0xFA, 0x96, 0x7B, 0x89, 0x7E };
        private static readonly byte[] Windows31JLeverage25Marker = { 0x83, 0x8C, 0x83, 0x6F, 0x83, 0x8C, 0x83, 0x62, 0x83, 0x57, 0x32, 0x35, 0x94, 0x7B };
        private static readonly byte[] Windows31JLeverage25CourseMarker = { 0x83, 0x8C, 0x83, 0x6F, 0x83, 0x8C, 0x83, 0x62, 0x83, 0x57, 0x32, 0x35, 0x94, 0x7B, 0x83, 0x52, 0x81, 0x5B, 0x83, 0x58 };
        private static readonly byte[] Windows31JHaMarker = { 0x82, 0xCD };
        private static readonly byte[] Windows31JCurrencyFromMarker = { 0x92, 0xCA, 0x89, 0xDD, 0x82, 0xA9, 0x82, 0xE7 };
        private static readonly byte[] Windows31JFullWidthPercentMarker = { 0x81, 0x93 };
        private static readonly byte[] SbiMarker = Encoding.ASCII.GetBytes("SBI");
        private static readonly byte[] FxMarker = Encoding.ASCII.GetBytes("FX");
        private static readonly byte[] HtmlListEndMarker = Encoding.ASCII.GetBytes("</li>");
        private static readonly byte[] HtmlParagraphEndMarker = Encoding.ASCII.GetBytes("</p>");

        private readonly string cachePathOverride;

        public SbiFxRuleService()
        {
        }

        public SbiFxRuleService(string cachePath)
        {
            cachePathOverride = cachePath;
        }

        public string CachePath => string.IsNullOrWhiteSpace(cachePathOverride)
            ? Path.Combine(Application.persistentDataPath, CacheFileName)
            : cachePathOverride;

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

        public void SaveLocal(SbiFxRuleSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.IsUsable)
            {
                throw new InvalidOperationException("SBI FX规则内容不完整，未写入本地缓存。");
            }

            string cachePath = CachePath;
            string cacheDirectory = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrWhiteSpace(cacheDirectory))
            {
                Directory.CreateDirectory(cacheDirectory);
            }

            string temporaryPath = cachePath + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(snapshot, true), CacheEncoding);
                if (File.Exists(cachePath))
                {
                    File.Delete(cachePath);
                }

                File.Move(temporaryPath, cachePath);
            }
            catch (Exception ex)
            {
                try
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch (Exception)
                {
                }

                throw new InvalidOperationException($"SBI FX规则保存失败：{ex.Message}", ex);
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

            SbiFxRuleSnapshot snapshot = ParseOfficialPage(
                request.downloadHandler.data,
                request.downloadHandler.text,
                DateTime.UtcNow);
            SaveLocal(snapshot);
            return snapshot;
        }

        public static SbiFxRuleSnapshot ParseOfficialPage(byte[] data, DateTime fetchedAtUtc)
        {
            return ParseOfficialPage(data, null, fetchedAtUtc);
        }

        public static SbiFxRuleSnapshot ParseOfficialPage(byte[] data, string fallbackText, DateTime fetchedAtUtc)
        {
            if (TryParseOfficialPageBytes(data, fetchedAtUtc, out SbiFxRuleSnapshot snapshot))
            {
                return snapshot;
            }

            string html = DecodeOfficialPage(data, fallbackText);
            return ParseOfficialPage(html, fetchedAtUtc);
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
            if (LooksLikeDecodedOfficialPage(fallback))
            {
                return fallback;
            }

            if (data == null || data.Length == 0)
            {
                return fallback ?? string.Empty;
            }

            foreach (string encodingName in new[] { "Windows-31J", "shift_jis", "932" })
            {
                try
                {
                    string decoded = Encoding.GetEncoding(encodingName).GetString(data);
                    if (LooksLikeDecodedOfficialPage(decoded))
                    {
                        return decoded;
                    }
                }
                catch (Exception)
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(fallback))
            {
                return fallback;
            }

            return Encoding.UTF8.GetString(data);
        }

        private static bool LooksLikeDecodedOfficialPage(string html)
        {
            return !string.IsNullOrWhiteSpace(html) &&
                   html.IndexOf("米ドル/日本円", StringComparison.Ordinal) >= 0 &&
                   html.IndexOf("レバレッジ25倍", StringComparison.Ordinal) >= 0;
        }

        private static bool TryParseOfficialPageBytes(
            byte[] data,
            DateTime fetchedAtUtc,
            out SbiFxRuleSnapshot snapshot)
        {
            snapshot = null;
            if (data == null || data.Length == 0)
            {
                return false;
            }

            if (!TryExtractApplicableDate(data, out string applicableDate) ||
                !TryExtractUsdJpyMargin(data, out int requiredMarginPer10000Jpy) ||
                !TryExtractMarginRatePercent(data, out double marginRatePercent) ||
                !TryExtractMinimumOrderUnits(data, out int minimumOrderUnits))
            {
                return false;
            }

            SbiFxRuleSnapshot parsed = new SbiFxRuleSnapshot
            {
                SourceUrl = OfficialRulesUrl,
                FetchedAtUtc = fetchedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                ApplicableDate = applicableDate,
                Pair = "USD/JPY",
                Leverage = 25,
                MarginRatePercent = marginRatePercent,
                RequiredMarginPer10000Jpy = requiredMarginPer10000Jpy,
                MinimumOrderUnits = minimumOrderUnits
            };

            if (!parsed.IsUsable)
            {
                return false;
            }

            snapshot = parsed;
            return true;
        }

        private static bool TryExtractApplicableDate(byte[] data, out string applicableDate)
        {
            applicableDate = null;
            int markerIndex = IndexOf(data, Windows31JApplicableDateMarker, 0, data.Length);
            if (markerIndex < 0)
            {
                return false;
            }

            int startIndex = Math.Max(0, markerIndex - 32);
            string dateSource = Encoding.ASCII.GetString(data, startIndex, markerIndex - startIndex);
            Match match = Regex.Match(dateSource, @"(\d{4}/\d{1,2}/\d{1,2})");
            if (!match.Success)
            {
                return false;
            }

            applicableDate = match.Groups[1].Value;
            return true;
        }

        private static bool TryExtractUsdJpyMargin(byte[] data, out int requiredMarginPer10000Jpy)
        {
            requiredMarginPer10000Jpy = 0;
            int pairIndex = IndexOf(data, Windows31JUsdJpyMarker, 0, data.Length);
            if (pairIndex < 0)
            {
                return false;
            }

            int rowEndIndex = IndexOf(data, HtmlListEndMarker, pairIndex, data.Length);
            if (rowEndIndex < 0)
            {
                rowEndIndex = Math.Min(data.Length, pairIndex + 12000);
            }

            int leverageIndex = IndexOf(data, Windows31JLeverage25Marker, pairIndex, rowEndIndex);
            if (leverageIndex < 0)
            {
                return false;
            }

            int valueStartIndex = FindByte(data, (byte)'>', leverageIndex, rowEndIndex);
            return valueStartIndex >= 0 &&
                   TryReadInteger(data, valueStartIndex + 1, rowEndIndex, out requiredMarginPer10000Jpy);
        }

        private static bool TryExtractMarginRatePercent(byte[] data, out double marginRatePercent)
        {
            marginRatePercent = 0d;
            int searchIndex = 0;
            while (searchIndex < data.Length)
            {
                int markerIndex = IndexOf(data, Windows31JLeverage25CourseMarker, searchIndex, data.Length);
                if (markerIndex < 0)
                {
                    return false;
                }

                int paragraphEndIndex = IndexOf(data, HtmlParagraphEndMarker, markerIndex, data.Length);
                int listEndIndex = IndexOf(data, HtmlListEndMarker, markerIndex, data.Length);
                int scanEndIndex = MinPositive(paragraphEndIndex, listEndIndex, Math.Min(data.Length, markerIndex + 240));
                if (ContainsPercentMarker(data, markerIndex, scanEndIndex) &&
                    TryReadDouble(
                        data,
                        markerIndex + Windows31JLeverage25CourseMarker.Length,
                        scanEndIndex,
                        out marginRatePercent))
                {
                    return true;
                }

                searchIndex = markerIndex + Windows31JLeverage25CourseMarker.Length;
            }

            return false;
        }

        private static bool TryExtractMinimumOrderUnits(byte[] data, out int minimumOrderUnits)
        {
            minimumOrderUnits = 0;
            int searchIndex = 0;
            while (searchIndex < data.Length)
            {
                int sbiIndex = IndexOf(data, SbiMarker, searchIndex, data.Length);
                if (sbiIndex < 0)
                {
                    return false;
                }

                int scanEndIndex = Math.Min(data.Length, sbiIndex + 96);
                int fxIndex = IndexOf(data, FxMarker, sbiIndex + SbiMarker.Length, scanEndIndex);
                int haIndex = fxIndex < 0
                    ? -1
                    : IndexOf(data, Windows31JHaMarker, fxIndex + FxMarker.Length, scanEndIndex);

                if (haIndex >= 0 &&
                    TryReadInteger(data, haIndex + Windows31JHaMarker.Length, scanEndIndex, out int units) &&
                    IndexOf(data, Windows31JCurrencyFromMarker, haIndex, scanEndIndex) >= 0)
                {
                    minimumOrderUnits = units;
                    return true;
                }

                searchIndex = sbiIndex + SbiMarker.Length;
            }

            return false;
        }

        private static bool ContainsPercentMarker(byte[] data, int startIndex, int endIndex)
        {
            return FindByte(data, (byte)'%', startIndex, endIndex) >= 0 ||
                   IndexOf(data, Windows31JFullWidthPercentMarker, startIndex, endIndex) >= 0;
        }

        private static bool TryReadInteger(byte[] data, int startIndex, int endIndex, out int value)
        {
            value = 0;
            int numberStartIndex = FindNumberStart(data, startIndex, endIndex);
            if (numberStartIndex < 0)
            {
                return false;
            }

            StringBuilder builder = new StringBuilder();
            for (int i = numberStartIndex; i < endIndex; i++)
            {
                byte current = data[i];
                if (current >= (byte)'0' && current <= (byte)'9')
                {
                    builder.Append((char)current);
                    continue;
                }

                if (current == (byte)',')
                {
                    continue;
                }

                break;
            }

            return builder.Length > 0 &&
                   int.TryParse(builder.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryReadDouble(byte[] data, int startIndex, int endIndex, out double value)
        {
            value = 0d;
            int numberStartIndex = FindNumberStart(data, startIndex, endIndex);
            if (numberStartIndex < 0)
            {
                return false;
            }

            StringBuilder builder = new StringBuilder();
            for (int i = numberStartIndex; i < endIndex; i++)
            {
                byte current = data[i];
                if ((current >= (byte)'0' && current <= (byte)'9') || current == (byte)'.')
                {
                    builder.Append((char)current);
                    continue;
                }

                if (current == (byte)',')
                {
                    continue;
                }

                break;
            }

            return builder.Length > 0 &&
                   double.TryParse(builder.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static int FindNumberStart(byte[] data, int startIndex, int endIndex)
        {
            for (int i = Math.Max(0, startIndex); i < endIndex && i < data.Length; i++)
            {
                if (data[i] >= (byte)'0' && data[i] <= (byte)'9')
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindByte(byte[] data, byte value, int startIndex, int endIndex)
        {
            for (int i = Math.Max(0, startIndex); i < endIndex && i < data.Length; i++)
            {
                if (data[i] == value)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int IndexOf(byte[] data, byte[] pattern, int startIndex, int endIndex)
        {
            if (data == null || pattern == null || pattern.Length == 0)
            {
                return -1;
            }

            int lastStartIndex = Math.Min(endIndex, data.Length) - pattern.Length;
            for (int i = Math.Max(0, startIndex); i <= lastStartIndex; i++)
            {
                bool matches = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (data[i + j] != pattern[j])
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int MinPositive(params int[] values)
        {
            int best = int.MaxValue;
            foreach (int value in values)
            {
                if (value >= 0 && value < best)
                {
                    best = value;
                }
            }

            return best == int.MaxValue ? -1 : best;
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
