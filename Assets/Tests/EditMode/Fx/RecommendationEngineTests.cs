using System;
using System.Collections.Generic;
using NUnit.Framework;
using TestFXTrade.Fx.Analysis;
using TestFXTrade.Fx.Domain;

namespace TestFXTrade.Tests.EditMode.Fx
{
    public sealed class RecommendationEngineTests
    {
        [Test]
        public void UsdJpyPipValueUsesQuoteCurrencyForJpyAccounts()
        {
            double pipValue = RiskCalculationService.GetPipValuePerLot(AccountCurrency.Jpy, 150d);

            Assert.AreEqual(1000d, pipValue);
        }

        [Test]
        public void UsdJpyMarginConvertsUsdNotionalToJpy()
        {
            double margin = RiskCalculationService.GetMarginPerLot(AccountCurrency.Jpy, 150d, 25d);

            Assert.AreEqual(600000d, margin);
        }

        [Test]
        public void BullishTrendProducesBuyRecommendationWhenRiskAllows()
        {
            RecommendationEngine engine = new RecommendationEngine();
            TradeRecommendation recommendation = engine.Build(
                new AccountSnapshot(1000000d, 1000000d, AccountCurrency.Jpy, 25d),
                new PositionSnapshot(0d, 0d, 0d, 0d),
                new RiskProfile(1d, 30d, 40d, 0.2d),
                new MarketQuote(FxConstants.UsdJpySymbol, 150d, DateTime.UtcNow, true, "Test"),
                BuildTrendingCandles(150d, 0.018d));

            Assert.AreEqual(RecommendationAction.Buy, recommendation.Action);
            Assert.GreaterOrEqual(recommendation.SuggestedBuyLots, FxConstants.MinTradableLot);
            Assert.Greater(recommendation.TrendScore, 0d);
        }

        [Test]
        public void StaleQuoteBlocksRecommendation()
        {
            RecommendationEngine engine = new RecommendationEngine();
            TradeRecommendation recommendation = engine.Build(
                new AccountSnapshot(1000000d, 1000000d, AccountCurrency.Jpy, 25d),
                new PositionSnapshot(0d, 0d, 0d, 0d),
                new RiskProfile(1d, 30d, 40d, 0.2d),
                new MarketQuote(FxConstants.UsdJpySymbol, 150d, DateTime.UtcNow.AddHours(-1), true, "Test"),
                BuildTrendingCandles(150d, 0.018d));

            Assert.AreEqual(RecommendationAction.Hold, recommendation.Action);
            Assert.That(recommendation.Summary, Does.Contain("过期"));
        }

        private static IReadOnlyList<Candle> BuildTrendingCandles(double start, double step)
        {
            List<Candle> candles = new List<Candle>();
            DateTime time = DateTime.UtcNow.AddMinutes(-120);

            for (int i = 0; i < 120; i++)
            {
                double close = start + (i * step);
                candles.Add(new Candle(
                    time.AddMinutes(i),
                    close - 0.006d,
                    close + 0.014d,
                    close - 0.014d,
                    close));
            }

            return candles;
        }
    }
}
