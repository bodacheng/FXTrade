using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TestFXTrade.Fx.Analysis;
using TestFXTrade.Fx.Domain;
using TestFXTrade.Fx.Sbi;

namespace TestFXTrade.Fx.OpenAI
{
    public static class OpenAiTradePromptBuilder
    {
        private const int PromptCandleCount = 32;

        public const string Instructions =
            "You are a conservative USD/JPY decision-support assistant. Return only the requested JSON schema. " +
            "Answer all natural-language fields in Simplified Chinese. Treat the SBI FX rule snapshot as a hard margin constraint, " +
            "not as a guarantee that a trade is safe. Choose BUY, SELL, or HOLD. suggested_lots is the size of the next order, " +
            "where 1 lot equals 100,000 base-currency units and HOLD must use 0. Never invent missing facts or prices. " +
            "Prefer HOLD when data is stale, insufficient, conflicting, or when the current position is already aggressive. " +
            "Keep estimated post-trade required margin at or below 50% of principal unless the order only reduces exposure. " +
            "Explicitly mention uncertainty and that the result is informational, not personalized investment advice. " +
            "Keep summary within 30 Chinese characters, reasoning within 90, and risk_warning within 60.";

        public static string Build(
            double principalJpy,
            double netPositionLots,
            MarketQuote quote,
            IReadOnlyList<Candle> candles,
            string interval,
            SbiFxRuleSnapshot rules)
        {
            if (double.IsNaN(principalJpy) || double.IsInfinity(principalJpy) || principalJpy <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(principalJpy), "本金必须大于0。 ");
            }

            if (double.IsNaN(netPositionLots) || double.IsInfinity(netPositionLots))
            {
                throw new ArgumentOutOfRangeException(nameof(netPositionLots), "净持仓必须是有限数值。");
            }

            if (quote == null || quote.Price <= 0d)
            {
                throw new ArgumentException("缺少有效的 USD/JPY 实时报价。", nameof(quote));
            }

            if (rules == null || !rules.IsUsable)
            {
                throw new ArgumentException("缺少有效的 SBI FX 本地规则。", nameof(rules));
            }

            double trendScore = TechnicalIndicatorService.CalculateTrendScore(candles, out double atrPips, out double rsi);
            double marginPerLot = rules.RequiredMarginPerStandardLotJpy;
            double currentEstimatedMargin = Math.Abs(netPositionLots) * marginPerLot;
            double conservativeGrossLotLimit = marginPerLot > 0d ? (principalJpy * 0.5d) / marginPerLot : 0d;

            StringBuilder prompt = new StringBuilder(4096);
            prompt.AppendLine("Generate one conservative USD/JPY order recommendation from this exact snapshot.");
            prompt.AppendLine();
            prompt.AppendLine("USER INPUTS");
            prompt.AppendLine(FormattableString.Invariant($"principal_jpy={principalJpy:0.##}"));
            prompt.AppendLine(FormattableString.Invariant($"net_position_lots={netPositionLots:0.###} (positive=long, negative=short, zero=flat)"));
            prompt.AppendLine();
            prompt.AppendLine("SBI FX RULES (downloaded locally from the official source)");
            prompt.AppendLine(rules.ToPromptText());
            prompt.AppendLine(FormattableString.Invariant($"required_margin_per_1_standard_lot={marginPerLot:0.##} JPY"));
            prompt.AppendLine(FormattableString.Invariant($"current_estimated_margin={currentEstimatedMargin:0.##} JPY"));
            prompt.AppendLine(FormattableString.Invariant($"conservative_50_percent_gross_limit={conservativeGrossLotLimit:0.###} lots"));
            prompt.AppendLine();
            prompt.AppendLine("LIVE MARKET SNAPSHOT");
            prompt.AppendLine(FormattableString.Invariant($"symbol={quote.Symbol}; price={quote.Price:0.000}; interval={interval}; quote_time_utc={quote.TimeUtc:O}; timestamp_reliable={quote.IsTimestampReliable}"));
            prompt.AppendLine(FormattableString.Invariant($"trend_score={trendScore:0.000}; rsi_14={rsi:0.0}; atr_14_pips={atrPips:0.0}; candle_count={(candles == null ? 0 : candles.Count)}"));
            prompt.AppendLine();
            prompt.AppendLine("RECENT CANDLES (oldest to newest)");
            AppendRecentCandles(prompt, candles);
            prompt.AppendLine();
            prompt.AppendLine("Return BUY or SELL only when the evidence is strong enough; otherwise return HOLD. " +
                              "The order must respect the SBI minimum order unit and the margin constraints above.");
            return prompt.ToString();
        }

        private static void AppendRecentCandles(StringBuilder prompt, IReadOnlyList<Candle> candles)
        {
            if (candles == null || candles.Count == 0)
            {
                prompt.AppendLine("none");
                return;
            }

            int start = Math.Max(0, candles.Count - PromptCandleCount);
            for (int i = start; i < candles.Count; i++)
            {
                Candle candle = candles[i];
                prompt.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:O},O={1:0.000},H={2:0.000},L={3:0.000},C={4:0.000}",
                    candle.TimeUtc,
                    candle.Open,
                    candle.High,
                    candle.Low,
                    candle.Close));
            }
        }
    }
}
