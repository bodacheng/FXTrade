using System;
using System.Collections.Generic;
using TestFXTrade.Fx.Domain;

namespace TestFXTrade.Fx.Analysis
{
    public sealed class RecommendationEngine
    {
        private const double TradeThreshold = 0.25d;
        private const double StrongInterventionRiskPrice = 160d;

        public TradeRecommendation Build(
            AccountSnapshot account,
            PositionSnapshot position,
            RiskProfile risk,
            MarketQuote quote,
            IReadOnlyList<Candle> candles)
        {
            TradeRecommendation result = new TradeRecommendation();

            if (account == null || position == null || risk == null || quote == null)
            {
                result.Action = RecommendationAction.Hold;
                result.Summary = "缺少账户、持仓、风控或报价参数。";
                result.Warnings.Add(result.Summary);
                return result;
            }

            result.LatestPrice = quote.Price;
            result.CurrentNetLots = position.NetLots;
            result.PipValuePerLotAccountCurrency = RiskCalculationService.GetPipValuePerLot(account.Currency, quote.Price);
            result.MarginPerLotAccountCurrency = RiskCalculationService.GetMarginPerLot(account.Currency, quote.Price, account.Leverage);

            double safeLotsByStop = RiskCalculationService.GetSafeLotsByStop(account, risk, result.PipValuePerLotAccountCurrency);
            double safeLotsByMargin = RiskCalculationService.GetSafeLotsByMargin(account, position, risk, result.MarginPerLotAccountCurrency);
            result.MaxSafeGrossLots = Math.Max(0d, Math.Min(safeLotsByStop, safeLotsByMargin));

            result.TrendScore = TechnicalIndicatorService.CalculateTrendScore(candles, out double atrPips, out double rsi);
            result.AtrPips = atrPips;
            result.Rsi = rsi;
            result.Confidence = Math.Abs(result.TrendScore);

            AddDataWarnings(result, quote, candles, risk);
            AddUsdJpyKnowledgeWarnings(result, quote.Price, atrPips);

            if (result.MaxSafeGrossLots < FxConstants.MinTradableLot)
            {
                result.Action = RecommendationAction.Hold;
                result.Summary = "观望：风险或保证金预算低于最小可交易 lot。";
                result.Warnings.Add("安全可交易量低于 0.01 lot。");
                return result;
            }

            if (result.Warnings.Exists(warning => warning.StartsWith("实时数据已过期", StringComparison.Ordinal)))
            {
                result.Action = RecommendationAction.Hold;
                result.Summary = "观望：实时报价已过期，无法安全计算交易量建议。";
                return result;
            }

            if (result.Confidence < TradeThreshold)
            {
                result.Action = RecommendationAction.Hold;
                result.TargetNetLots = position.NetLots;
                result.Summary = "观望：USD/JPY 趋势信号不够强。";
                result.Reasons.Add("EMA、RSI、ATR 与短期动量信号未形成明确共识。");
                return result;
            }

            double exposureMultiplier = GetExposureMultiplier(result.TrendScore, quote.Price, atrPips, risk.EstimatedSpreadPips);
            double direction = result.TrendScore > 0d ? 1d : -1d;
            result.TargetNetLots = RoundLot(result.MaxSafeGrossLots * exposureMultiplier * direction);

            double delta = result.TargetNetLots - position.NetLots;
            if (Math.Abs(delta) < FxConstants.MinTradableLot)
            {
                result.Action = RecommendationAction.Hold;
                result.Summary = "观望：当前净头寸已接近计算目标。";
                result.Reasons.Add($"目标净头寸为 {result.TargetNetLots:0.00} lots。");
                return result;
            }

            if (delta > 0d)
            {
                result.Action = RecommendationAction.Buy;
                result.SuggestedBuyLots = RoundLot(delta);
                result.RequiredMarginForSuggestion = result.SuggestedBuyLots * result.MarginPerLotAccountCurrency;
                result.Summary = $"建议买入最多 {result.SuggestedBuyLots:0.00} lots USD/JPY。";
            }
            else
            {
                result.Action = RecommendationAction.Sell;
                result.SuggestedSellLots = RoundLot(Math.Abs(delta));
                result.RequiredMarginForSuggestion = result.SuggestedSellLots * result.MarginPerLotAccountCurrency;
                result.Summary = $"建议卖出或减少 {result.SuggestedSellLots:0.00} lots USD/JPY 净多头。";
            }

            result.Reasons.Add($"趋势评分为 {result.TrendScore:0.00}，依据 EMA、RSI、ATR、动量与斜率计算。");
            result.Reasons.Add($"风险上限允许止损侧 {safeLotsByStop:0.00} lots、保证金侧 {safeLotsByMargin:0.00} lots。");
            result.Reasons.Add($"计划止损为 {risk.PlannedStopLossPips:0.#} pips，ATR 为 {atrPips:0.#} pips。");
            return result;
        }

        private static void AddDataWarnings(TradeRecommendation result, MarketQuote quote, IReadOnlyList<Candle> candles, RiskProfile risk)
        {
            if (candles == null || candles.Count < 60)
            {
                result.Warnings.Add("可用K线少于 60 根，信号质量有所下降。");
            }

            if (quote.IsTimestampReliable)
            {
                double staleMinutes = (DateTime.UtcNow - quote.TimeUtc).TotalMinutes;
                if (staleMinutes > 15d)
                {
                    result.Warnings.Add($"实时数据已过期 {staleMinutes:0.#} 分钟。");
                }
            }
            else
            {
                result.Warnings.Add("数据源无法验证报价时间戳。");
            }

            if (risk.EstimatedSpreadPips > 3d)
            {
                result.Warnings.Add("USD/JPY 预估点差较高，建议交易量已降低。");
            }
        }

        private static void AddUsdJpyKnowledgeWarnings(TradeRecommendation result, double price, double atrPips)
        {
            if (price >= StrongInterventionRiskPrice)
            {
                result.Warnings.Add("USD/JPY 高于 160，日元干预相关消息风险上升。");
            }

            if (atrPips >= 35d)
            {
                result.Warnings.Add("短期 ATR 较高，按波动率调整后的交易量已降低。");
            }
        }

        private static double GetExposureMultiplier(double trendScore, double price, double atrPips, double spreadPips)
        {
            double multiplier = Math.Min(1d, Math.Max(0.25d, Math.Abs(trendScore)));

            if (price >= StrongInterventionRiskPrice)
            {
                multiplier *= 0.65d;
            }

            if (atrPips >= 35d)
            {
                multiplier *= 0.7d;
            }

            if (spreadPips > 3d)
            {
                multiplier *= 0.65d;
            }

            return multiplier;
        }

        private static double RoundLot(double lots)
        {
            return Math.Floor(Math.Max(0d, lots) * 100d) / 100d;
        }
    }
}
