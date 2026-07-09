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
                result.Summary = "Missing account, position, risk, or quote input.";
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
                result.Summary = "Hold: risk or margin budget is below the minimum practical lot size.";
                result.Warnings.Add("Safe lot capacity is below 0.01 lot.");
                return result;
            }

            if (result.Warnings.Exists(warning => warning.StartsWith("Live data is stale", StringComparison.Ordinal)))
            {
                result.Action = RecommendationAction.Hold;
                result.Summary = "Hold: live price is stale, so no trading-size recommendation is safe.";
                return result;
            }

            if (result.Confidence < TradeThreshold)
            {
                result.Action = RecommendationAction.Hold;
                result.TargetNetLots = position.NetLots;
                result.Summary = "Hold: USD/JPY trend signal is not strong enough.";
                result.Reasons.Add("EMA, RSI, ATR, and short momentum do not agree strongly.");
                return result;
            }

            double exposureMultiplier = GetExposureMultiplier(result.TrendScore, quote.Price, atrPips, risk.EstimatedSpreadPips);
            double direction = result.TrendScore > 0d ? 1d : -1d;
            result.TargetNetLots = RoundLot(result.MaxSafeGrossLots * exposureMultiplier * direction);

            double delta = result.TargetNetLots - position.NetLots;
            if (Math.Abs(delta) < FxConstants.MinTradableLot)
            {
                result.Action = RecommendationAction.Hold;
                result.Summary = "Hold: current net position is already close to the calculated target.";
                result.Reasons.Add($"Target net position is {result.TargetNetLots:0.00} lots.");
                return result;
            }

            if (delta > 0d)
            {
                result.Action = RecommendationAction.Buy;
                result.SuggestedBuyLots = RoundLot(delta);
                result.RequiredMarginForSuggestion = result.SuggestedBuyLots * result.MarginPerLotAccountCurrency;
                result.Summary = $"Buy up to {result.SuggestedBuyLots:0.00} lots of USD/JPY.";
            }
            else
            {
                result.Action = RecommendationAction.Sell;
                result.SuggestedSellLots = RoundLot(Math.Abs(delta));
                result.RequiredMarginForSuggestion = result.SuggestedSellLots * result.MarginPerLotAccountCurrency;
                result.Summary = $"Sell or reduce net long exposure by {result.SuggestedSellLots:0.00} lots of USD/JPY.";
            }

            result.Reasons.Add($"Trend score is {result.TrendScore:0.00}, based on EMA, RSI, ATR, momentum, and slope.");
            result.Reasons.Add($"Risk cap allows {safeLotsByStop:0.00} lots by stop-loss and {safeLotsByMargin:0.00} lots by margin.");
            result.Reasons.Add($"Planned stop is {risk.PlannedStopLossPips:0.#} pips; ATR is {atrPips:0.#} pips.");
            return result;
        }

        private static void AddDataWarnings(TradeRecommendation result, MarketQuote quote, IReadOnlyList<Candle> candles, RiskProfile risk)
        {
            if (candles == null || candles.Count < 60)
            {
                result.Warnings.Add("Less than 60 candles were available; signal quality is reduced.");
            }

            if (quote.IsTimestampReliable)
            {
                double staleMinutes = (DateTime.UtcNow - quote.TimeUtc).TotalMinutes;
                if (staleMinutes > 15d)
                {
                    result.Warnings.Add($"Live data is stale by {staleMinutes:0.#} minutes.");
                }
            }
            else
            {
                result.Warnings.Add("Quote timestamp could not be verified by the data provider.");
            }

            if (risk.EstimatedSpreadPips > 3d)
            {
                result.Warnings.Add("Estimated spread is high for USD/JPY; suggested size is reduced.");
            }
        }

        private static void AddUsdJpyKnowledgeWarnings(TradeRecommendation result, double price, double atrPips)
        {
            if (price >= StrongInterventionRiskPrice)
            {
                result.Warnings.Add("USD/JPY is above 160; yen intervention headline risk is elevated.");
            }

            if (atrPips >= 35d)
            {
                result.Warnings.Add("Short-term ATR is high; volatility-adjusted size is reduced.");
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
