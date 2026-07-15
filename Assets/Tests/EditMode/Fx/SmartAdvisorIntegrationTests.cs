using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TestFXTrade.Fx.Domain;
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
        public void ParsesStructuredOpenAiAdviceFromResponsesApi()
        {
            const string responseJson =
                "{\"output\":[{\"content\":[{\"type\":\"output_text\",\"text\":\"{\\\"action\\\":\\\"BUY\\\",\\\"suggested_lots\\\":0.01,\\\"confidence\\\":0.72,\\\"summary\\\":\\\"短线偏多\\\",\\\"reasoning\\\":\\\"趋势与动量一致\\\",\\\"risk_warning\\\":\\\"波动扩大时观望\\\"}\"}]}]}";

            OpenAiTradeAdvice advice = OpenAiTradeAdvisorClient.ParseResponse(responseJson);

            Assert.AreEqual("BUY", advice.action);
            Assert.AreEqual(0.01d, advice.suggested_lots);
            Assert.AreEqual(0.72d, advice.confidence);
            Assert.AreEqual("短线偏多", advice.summary);
        }

        [Test]
        public void OpenAiRequestUsesStrictResponsesJsonSchema()
        {
            OpenAiTradeAdvisorClient client = new OpenAiTradeAdvisorClient("test-key");
            MethodInfo buildRequest = typeof(OpenAiTradeAdvisorClient).GetMethod(
                "BuildRequest",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(buildRequest);
            object payload = buildRequest.Invoke(client, new object[] { "test prompt" });
            string json = JsonUtility.ToJson(payload);

            Assert.That(json, Does.Contain("\"model\":\"gpt-5.6\""));
            Assert.That(json, Does.Contain("\"store\":false"));
            Assert.That(json, Does.Contain("\"type\":\"json_schema\""));
            Assert.That(json, Does.Contain("\"strict\":true"));
            Assert.That(json, Does.Contain("\"enum\":[\"BUY\",\"SELL\",\"HOLD\"]"));
            Assert.That(json, Does.Not.Contain("\"suggested_lots\":{\"type\":\"number\",\"enum\""));
            Assert.That(json, Does.Not.Contain("test-key"));
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
