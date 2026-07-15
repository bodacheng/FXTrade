using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TestFXTrade.Fx.Domain;
using TestFXTrade.Fx.MarketData;
using TestFXTrade.Fx.OpenAI;
using TestFXTrade.Fx.Sbi;
using UnityEngine;

namespace TestFXTrade.Tests.EditMode.Fx
{
    public sealed class SmartAdvisorIntegrationTests
    {
        [Test]
        public void ParsesOfficialSbiUsdJpyMarginSnapshot()
        {
            const string html = @"
                <div>(2026/7/14適用分：1万通貨あたり)</div>
                <div class='fx_hyou_row'>
                    <div>米ドル/日本円</div>
                    <div data-label='レバレッジ1倍'>1,624,550</div>
                    <div data-label='レバレッジ25倍'>64,982</div>
                </div>
                <p>レバレッジ25倍コース：4％</p>
                <p>外国為替保証金取引(SBI　FX)は100通貨からとなります。</p>";

            SbiFxRuleSnapshot snapshot = SbiFxRuleService.ParseOfficialPage(
                html,
                new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc));

            Assert.AreEqual("2026/7/14", snapshot.ApplicableDate);
            Assert.AreEqual(25, snapshot.Leverage);
            Assert.AreEqual(4d, snapshot.MarginRatePercent);
            Assert.AreEqual(64982, snapshot.RequiredMarginPer10000Jpy);
            Assert.AreEqual(100, snapshot.MinimumOrderUnits);
            Assert.AreEqual(649820d, snapshot.RequiredMarginPerStandardLotJpy);
        }

        [Test]
        public void PromptContainsOnlyRequestedAccountInputsAndLocalSbiRules()
        {
            SbiFxRuleSnapshot rules = BuildRules();
            MarketQuote quote = new MarketQuote(
                FxConstants.UsdJpySymbol,
                158.125d,
                new DateTime(2026, 7, 14, 1, 2, 3, DateTimeKind.Utc),
                true,
                "Test");

            string prompt = OpenAiTradePromptBuilder.Build(
                1000000d,
                -0.25d,
                quote,
                BuildCandles(),
                "5min",
                rules);

            Assert.That(prompt, Does.Contain("principal_jpy=1000000"));
            Assert.That(prompt, Does.Contain("net_position_lots=-0.25"));
            Assert.That(prompt, Does.Contain("required_margin_per_10,000_currency=64982 JPY"));
            Assert.That(prompt, Does.Contain("required_margin_per_1_standard_lot=649820 JPY"));
            Assert.That(prompt, Does.Not.Contain("OPENAI_API_KEY"));
        }

        [Test]
        public void ParsesStructuredAdviceFromAzureRelay()
        {
            const string responseJson =
                "{\"action\":\"BUY\",\"suggested_lots\":0.01,\"confidence\":0.72," +
                "\"summary\":\"短线偏多\",\"reasoning\":\"趋势与动量一致\",\"risk_warning\":\"波动扩大时观望\"}";

            OpenAiTradeAdvice advice = AzureRelayTradeAdvisorClient.ParseResponse(responseJson);

            Assert.AreEqual("BUY", advice.action);
            Assert.AreEqual(0.01d, advice.suggested_lots);
            Assert.AreEqual(0.72d, advice.confidence);
            Assert.AreEqual("短线偏多", advice.summary);
        }

        [Test]
        public void AzureRelayRequestContainsOnlyPromptAndMode()
        {
            MethodInfo buildRequest = typeof(AzureRelayTradeAdvisorClient).GetMethod(
                "BuildRequest",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(AiTradeAdviceMode) },
                null);

            Assert.NotNull(buildRequest);
            object payload = buildRequest.Invoke(
                null,
                new object[] { "test prompt", AiTradeAdviceMode.Conservative });
            string json = JsonUtility.ToJson(payload);

            Assert.That(json, Does.Contain("\"prompt\":\"test prompt\""));
            Assert.That(json, Does.Contain("\"mode\":\"conservative\""));
            Assert.That(json, Does.Not.Contain("OPENAI_API_KEY"));
            Assert.That(json, Does.Not.Contain("TWELVE_DATA_API_KEY"));
            Assert.That(json, Does.Not.Contain("Authorization"));
        }

        [Test]
        public void ForcedDirectionalModeRequiresBuyOrSellAndUsesHigherControlledLimit()
        {
            SbiFxRuleSnapshot rules = BuildRules();
            MarketQuote quote = new MarketQuote(
                FxConstants.UsdJpySymbol,
                158.125d,
                new DateTime(2026, 7, 14, 1, 2, 3, DateTimeKind.Utc),
                true,
                "Test");

            string prompt = OpenAiTradePromptBuilder.Build(
                1000000d,
                0d,
                quote,
                BuildCandles(),
                "5min",
                rules,
                AiTradeAdviceMode.ForcedDirectional);

            Assert.That(prompt, Does.Contain("decision_mode=forced_directional"));
            Assert.That(prompt, Does.Contain("margin_usage_limit_percent=70"));
            Assert.That(prompt, Does.Contain("must return BUY or SELL, never HOLD"));

            MethodInfo buildRequest = typeof(AzureRelayTradeAdvisorClient).GetMethod(
                "BuildRequest",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(AiTradeAdviceMode) },
                null);
            object payload = buildRequest.Invoke(
                null,
                new object[] { prompt, AiTradeAdviceMode.ForcedDirectional });
            string json = JsonUtility.ToJson(payload);

            Assert.That(json, Does.Contain("\"mode\":\"forced_directional\""));
        }

        [Test]
        public void ForcedDirectionalLocalGuardProducesExecutableMinimumOrder()
        {
            OpenAiTradeAdvice advice = new OpenAiTradeAdvice
            {
                action = "BUY",
                suggested_lots = 0d,
                confidence = 0.3d,
                summary = "弱多头",
                reasoning = "方向信号较弱",
                risk_warning = "低置信度"
            };
            MethodInfo applyGuard = typeof(TestFXTrade.Fx.UI.FxTradeAdvisorApp).GetMethod(
                "ApplyLocalMarginGuard",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(applyGuard);
            object adjusted = applyGuard.Invoke(
                null,
                new object[]
                {
                    advice,
                    1000000d,
                    0d,
                    BuildRules(),
                    AiTradeAdviceMode.ForcedDirectional
                });

            Assert.IsTrue((bool)adjusted);
            Assert.AreEqual("BUY", advice.action);
            Assert.AreEqual(0.001d, advice.suggested_lots, 0.0000001d);
        }

        [Test]
        public void AzureRelaySettingsRequireTlsExceptForLoopbackDevelopment()
        {
            Assert.AreEqual(
                "https://example.azurewebsites.net",
                AzureRelaySettings.NormalizeBaseUrl("https://example.azurewebsites.net/"));
            Assert.AreEqual(
                "http://localhost:7071",
                AzureRelaySettings.NormalizeBaseUrl("http://localhost:7071/"));
            Assert.AreEqual(string.Empty, AzureRelaySettings.NormalizeBaseUrl("http://example.com"));
            Assert.AreEqual(
                string.Empty,
                AzureRelaySettings.NormalizeBaseUrl("https://YOUR_FUNCTION_APP.azurewebsites.net"));
        }

        private static SbiFxRuleSnapshot BuildRules()
        {
            return new SbiFxRuleSnapshot
            {
                SourceUrl = SbiFxRuleService.OfficialRulesUrl,
                FetchedAtUtc = "2026-07-14T00:00:00.0000000Z",
                ApplicableDate = "2026/7/14",
                Pair = "USD/JPY",
                Leverage = 25,
                MarginRatePercent = 4d,
                RequiredMarginPer10000Jpy = 64982,
                MinimumOrderUnits = 100
            };
        }

        private static IReadOnlyList<Candle> BuildCandles()
        {
            List<Candle> candles = new List<Candle>();
            DateTime start = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < 80; i++)
            {
                double close = 157d + (i * 0.01d);
                candles.Add(new Candle(start.AddMinutes(i * 5), close - 0.01d, close + 0.02d, close - 0.02d, close));
            }

            return candles;
        }
    }
}
