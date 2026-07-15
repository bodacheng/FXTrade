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

        private const string CommonInstructions =
            "You are a USD/JPY decision-support assistant. Return only the requested JSON schema. " +
            "Answer all natural-language fields in Simplified Chinese. Treat the SBI FX rule snapshot as a hard margin constraint, " +
            "not as a guarantee that a trade is safe. The JSON field suggested_lots remains the internal next-order size in " +
            "standard lots, where 1 standard lot equals 100,000 base-currency units. In summary, reasoning, and risk_warning, " +
            "describe size as 建玉数量 in base-currency units (通貨), not as lot or 手数. Never invent missing facts or prices. Respect the mode-specific margin limit. " +
            "Explicitly mention uncertainty and that the result is informational, not personalized investment advice. " +
            "Keep summary within 30 Chinese characters, reasoning within 90, and risk_warning within 60.";

        public static string GetInstructions(AiTradeAdviceMode mode)
        {
            if (mode == AiTradeAdviceMode.ForcedDirectional)
            {
                return CommonInstructions +
                    " This is a relatively aggressive forced-direction scenario. You must choose BUY or SELL and never HOLD. " +
                    "When evidence is weak or conflicting, choose the more defensible direction, lower confidence, and use a smaller feasible order. " +
                    "Do not use forced direction as a reason to ignore uncertainty, the SBI minimum order, or the 70% margin limit.";
            }

            return CommonInstructions +
                " This is the conservative scenario. Choose BUY, SELL, or HOLD; HOLD must use suggested_lots=0. " +
                "Prefer HOLD when data is stale, insufficient, conflicting, or when the current position is already aggressive. " +
                "Keep estimated post-trade required margin at or below 50% of principal unless the order only reduces exposure.";
        }

        public static double GetMarginLimitRatio(AiTradeAdviceMode mode)
        {
            return mode == AiTradeAdviceMode.ForcedDirectional ? 0.7d : 0.5d;
        }

        public static string Build(
            double principalJpy,
            double netPositionLots,
            MarketQuote quote,
            IReadOnlyList<Candle> candles,
            string interval,
            SbiFxRuleSnapshot rules)
        {
            return Build(
                principalJpy,
                netPositionLots,
                quote,
                candles,
                interval,
                rules,
                AiTradeAdviceMode.Conservative);
        }

        public static string Build(
            double principalJpy,
            double netPositionLots,
            MarketQuote quote,
            IReadOnlyList<Candle> candles,
            string interval,
            SbiFxRuleSnapshot rules,
            AiTradeAdviceMode mode)
        {
            if (double.IsNaN(principalJpy) || double.IsInfinity(principalJpy) || principalJpy <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(principalJpy), "本金必须大于0。 ");
            }

            if (double.IsNaN(netPositionLots) || double.IsInfinity(netPositionLots))
            {
                throw new ArgumentOutOfRangeException(nameof(netPositionLots), "净建玉数量必须是有限数值。");
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
            double marginLimitRatio = GetMarginLimitRatio(mode);
            double grossLotLimit = marginPerLot > 0d ? (principalJpy * marginLimitRatio) / marginPerLot : 0d;
            double maximumBuyOrderLots = Math.Max(0d, grossLotLimit - netPositionLots);
            double maximumSellOrderLots = Math.Max(0d, grossLotLimit + netPositionLots);
            double netPositionQuantity = netPositionLots * FxConstants.StandardLotBaseUnits;
            double grossPositionLimitQuantity = grossLotLimit * FxConstants.StandardLotBaseUnits;
            double maximumBuyOrderQuantity = maximumBuyOrderLots * FxConstants.StandardLotBaseUnits;
            double maximumSellOrderQuantity = maximumSellOrderLots * FxConstants.StandardLotBaseUnits;
            int marginLimitPercent = (int)Math.Round(marginLimitRatio * 100d);
            string modeLabel = mode == AiTradeAdviceMode.ForcedDirectional ? "forced_directional" : "conservative";

            StringBuilder prompt = new StringBuilder(4096);
            prompt.AppendLine("Generate one USD/JPY order recommendation from this exact snapshot.");
            prompt.AppendLine(FormattableString.Invariant($"decision_mode={modeLabel}"));
            prompt.AppendLine();
            prompt.AppendLine("USER INPUTS");
            prompt.AppendLine(FormattableString.Invariant($"principal_jpy={principalJpy:0.##}"));
            prompt.AppendLine(FormattableString.Invariant($"net_position_quantity={netPositionQuantity:0.##} base_currency_units (positive=long, negative=short, zero=flat)"));
            prompt.AppendLine(FormattableString.Invariant($"net_position_lots={netPositionLots:0.###} (internal schema value; do not mention lots in natural-language fields)"));
            prompt.AppendLine();
            prompt.AppendLine("SBI FX RULES (downloaded locally from the official source)");
            prompt.AppendLine(rules.ToPromptText());
            prompt.AppendLine(FormattableString.Invariant($"required_margin_per_1_standard_lot={marginPerLot:0.##} JPY"));
            prompt.AppendLine(FormattableString.Invariant($"current_estimated_margin={currentEstimatedMargin:0.##} JPY"));
            prompt.AppendLine(FormattableString.Invariant($"margin_usage_limit_percent={marginLimitPercent}"));
            prompt.AppendLine(FormattableString.Invariant($"gross_position_limit_quantity={grossPositionLimitQuantity:0.##} base_currency_units"));
            prompt.AppendLine(FormattableString.Invariant($"maximum_buy_order_quantity={maximumBuyOrderQuantity:0.##} base_currency_units"));
            prompt.AppendLine(FormattableString.Invariant($"maximum_sell_order_quantity={maximumSellOrderQuantity:0.##} base_currency_units"));
            prompt.AppendLine(FormattableString.Invariant($"gross_position_limit={grossLotLimit:0.###} lots"));
            prompt.AppendLine(FormattableString.Invariant($"maximum_buy_order={maximumBuyOrderLots:0.###} lots"));
            prompt.AppendLine(FormattableString.Invariant($"maximum_sell_order={maximumSellOrderLots:0.###} lots"));
            prompt.AppendLine();
            prompt.AppendLine("LIVE MARKET SNAPSHOT");
            prompt.AppendLine(FormattableString.Invariant($"symbol={quote.Symbol}; price={quote.Price:0.000}; interval={interval}; quote_time_utc={quote.TimeUtc:O}; timestamp_reliable={quote.IsTimestampReliable}"));
            prompt.AppendLine(FormattableString.Invariant($"trend_score={trendScore:0.000}; rsi_14={rsi:0.0}; atr_14_pips={atrPips:0.0}; candle_count={(candles == null ? 0 : candles.Count)}"));
            prompt.AppendLine();
            prompt.AppendLine("RECENT CANDLES (oldest to newest)");
            AppendRecentCandles(prompt, candles);
            prompt.AppendLine();
            if (mode == AiTradeAdviceMode.ForcedDirectional)
            {
                prompt.AppendLine("You must return BUY or SELL, never HOLD. If the signal is weak, express that through lower confidence, " +
                                  "a smaller feasible order, and an explicit risk warning. Respect the SBI minimum order and 70% margin limit.");
            }
            else
            {
                prompt.AppendLine("Return BUY or SELL only when the evidence is strong enough; otherwise return HOLD. " +
                                  "The order must respect the SBI minimum order unit and the 50% margin limit.");
            }

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
