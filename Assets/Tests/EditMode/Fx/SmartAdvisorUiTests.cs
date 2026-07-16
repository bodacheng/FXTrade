using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TestFXTrade.Fx.Domain;
using TestFXTrade.Fx.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

namespace TestFXTrade.Tests.EditMode.Fx
{
    public sealed class SmartAdvisorUiTests
    {
        [Test]
        public void PortraitAdvisorUsesTmpAndNonBlockingLoadingWithinReferenceHeight()
        {
            EventSystem existingEventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            GameObject host = new GameObject("UI Test Host");
            GameObject canvasObject = null;

            try
            {
                FxTradeAdvisorApp app = host.AddComponent<FxTradeAdvisorApp>();
                MethodInfo awake = typeof(FxTradeAdvisorApp).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(awake);
                awake.Invoke(app, null);
                canvasObject = GameObject.Find("USDJPY Advisor Canvas");
                Assert.NotNull(canvasObject);

                Canvas.ForceUpdateCanvases();
                Assert.NotNull(Resources.Load<Font>("Fonts/NotoSansSC-Regular"));
                FieldInfo fontField = typeof(FxTradeAdvisorApp).GetField("font", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(fontField);
                TMP_FontAsset activeFont = (TMP_FontAsset)fontField.GetValue(app);
                Assert.NotNull(activeFont);
                Assert.That(activeFont.name, Does.Contain("NotoSansSC Bundled"));
                Assert.IsTrue(activeFont.HasCharacters("中文建玉数量通貨买卖同步", out System.Collections.Generic.List<char> loadedMissingCharacters), string.Join(string.Empty, loadedMissingCharacters));
                Assert.IsTrue(activeFont.TryAddCharacters("震荡风险支撑阻力美元日元", out string missingCharacters, true), missingCharacters);
                Assert.IsEmpty(missingCharacters);
                Assert.IsTrue(activeFont.TryAddCharacters("日本語取引アドバイザー保証金更新", out missingCharacters, true), missingCharacters);
                Assert.IsEmpty(missingCharacters);

                TMP_InputField[] accountInputs = canvasObject.GetComponentsInChildren<TMP_InputField>(true);
                Assert.AreEqual(3, accountInputs.Length);
                int passwordInputCount = 0;
                for (int i = 0; i < accountInputs.Length; i++)
                {
                    if (accountInputs[i].contentType == TMP_InputField.ContentType.Password)
                    {
                        passwordInputCount++;
                    }
                }

                Assert.AreEqual(0, passwordInputCount);
                Assert.AreEqual(0, canvasObject.GetComponentsInChildren<Text>(true).Length);
                Assert.AreEqual(0, canvasObject.GetComponentsInChildren<InputField>(true).Length);
                Assert.AreEqual(0, canvasObject.GetComponentsInChildren<Dropdown>(true).Length);
                Assert.Greater(canvasObject.GetComponentsInChildren<TextMeshProUGUI>(true).Length, 0);
                Assert.AreEqual(2, canvasObject.GetComponentsInChildren<TMP_Dropdown>(true).Length);
                Assert.Greater(canvasObject.GetComponentsInChildren<LocalizeStringEvent>(true).Length, 0);
                TMP_Text[] visibleTexts = canvasObject.GetComponentsInChildren<TMP_Text>(true);
                bool hasPositionQuantityLabel = false;
                for (int i = 0; i < visibleTexts.Length; i++)
                {
                    if (visibleTexts[i].text.Contains("净建玉数量"))
                    {
                        hasPositionQuantityLabel = true;
                    }

                    Assert.That(visibleTexts[i].text, Does.Not.Contain("lot"));
                }

                Assert.IsTrue(hasPositionQuantityLabel);
                Button[] buttons = canvasObject.GetComponentsInChildren<Button>(true);
                Assert.AreEqual(4, buttons.Length);

                accountInputs[0].SetTextWithoutNotify("1,000,000");
                accountInputs[1].SetTextWithoutNotify("-10,000");
                accountInputs[2].SetTextWithoutNotify("158.250");
                MethodInfo tryReadInputs = typeof(FxTradeAdvisorApp).GetMethod("TryReadAdvisorInputs", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(tryReadInputs);
                object[] inputArgs = { 0d, 0d, 0d };
                Assert.IsTrue((bool)tryReadInputs.Invoke(app, inputArgs));
                Assert.AreEqual(1000000d, (double)inputArgs[0]);
                Assert.AreEqual(-0.1d, (double)inputArgs[1], 0.0000001d);
                Assert.AreEqual(158.250d, (double)inputArgs[2], 0.0000001d);

                MethodInfo renderMarketMetrics = typeof(FxTradeAdvisorApp).GetMethod("RenderMarketMetrics", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(renderMarketMetrics);
                renderMarketMetrics.Invoke(
                    app,
                    new object[]
                    {
                        new MarketQuote("USD/JPY", 158.300d, new DateTime(2026, 7, 15, 1, 2, 3, DateTimeKind.Utc), true, "Test"),
                        new List<Candle>
                        {
                            new Candle(new DateTime(2026, 7, 15, 1, 0, 0, DateTimeKind.Utc), 158.2d, 158.3d, 158.1d, 158.25d),
                            new Candle(new DateTime(2026, 7, 15, 1, 5, 0, DateTimeKind.Utc), 158.25d, 158.35d, 158.2d, 158.3d)
                        },
                        "5min"
                    });
                FieldInfo metricsField = typeof(FxTradeAdvisorApp).GetField("metricsText", BindingFlags.Instance | BindingFlags.NonPublic);
                TMP_Text metricsText = (TMP_Text)metricsField.GetValue(app);
                Assert.That(metricsText.text, Does.Contain("负数=卖出"));
                Assert.That(metricsText.text, Does.Contain("建仓价 USD/JPY 158.250"));

                MethodInfo normalizeAdviceText = typeof(FxTradeAdvisorApp).GetMethod("NormalizeVisibleAdviceText", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.NotNull(normalizeAdviceText);
                string normalized = (string)normalizeAdviceText.Invoke(null, new object[] { "建议 0.01 lot，手数较低" });
                Assert.That(normalized, Does.Contain("建玉数量 1,000 通貨"));
                Assert.That(normalized, Does.Not.Contain("lot"));
                Assert.That(normalized, Does.Not.Contain("手数"));

                Transform loadingIndicator = canvasObject.transform.Find("Root/Safe Area Content/Loading Indicator");
                Assert.NotNull(loadingIndicator);
                Assert.IsFalse(loadingIndicator.gameObject.activeSelf);
                Image indicatorBackground = loadingIndicator.GetComponent<Image>();
                Assert.NotNull(indicatorBackground);
                Assert.IsFalse(indicatorBackground.raycastTarget);
                Assert.NotNull(loadingIndicator.Find("Loading Spinner"));
                RectTransform indicatorRect = loadingIndicator.GetComponent<RectTransform>();
                Assert.LessOrEqual(indicatorRect.sizeDelta.x, 40f);
                Assert.LessOrEqual(indicatorRect.sizeDelta.y, 40f);
                Assert.IsTrue(loadingIndicator.GetComponent<LayoutElement>().ignoreLayout);

                MethodInfo beginLoading = typeof(FxTradeAdvisorApp).GetMethod("BeginLoading", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo endLoading = typeof(FxTradeAdvisorApp).GetMethod("EndLoading", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(beginLoading);
                Assert.NotNull(endLoading);

                beginLoading.Invoke(app, new object[] { "status_processing", "正在测试加载状态", Array.Empty<object>() });
                beginLoading.Invoke(app, new object[] { "status_processing", "正在测试嵌套任务", Array.Empty<object>() });
                Assert.IsTrue(loadingIndicator.gameObject.activeSelf);
                for (int i = 0; i < buttons.Length; i++)
                {
                    Assert.IsTrue(buttons[i].interactable);
                }

                endLoading.Invoke(app, null);
                Assert.IsTrue(loadingIndicator.gameObject.activeSelf);
                endLoading.Invoke(app, null);
                Assert.IsFalse(loadingIndicator.gameObject.activeSelf);

                Transform safeArea = canvasObject.transform.Find("Root/Safe Area Content");
                Assert.NotNull(safeArea);
                Assert.IsNull(safeArea.GetComponent<ScrollRect>());

                RectTransform safeAreaRect = safeArea.GetComponent<RectTransform>();
                float preferredHeight = LayoutUtility.GetPreferredHeight(safeAreaRect);
                Assert.LessOrEqual(preferredHeight, 844f);

                Transform chartPanel = safeArea.Find("ChartPanel");
                Transform adviceText = safeArea.Find("AI Advice Text");
                Assert.NotNull(chartPanel);
                Assert.NotNull(adviceText);

                LayoutElement chartLayout = chartPanel.GetComponent<LayoutElement>();
                LayoutElement adviceLayout = adviceText.GetComponent<LayoutElement>();
                Assert.Less(chartLayout.preferredHeight, adviceLayout.preferredHeight);
                Assert.AreEqual(0f, chartLayout.flexibleHeight);
                Assert.Greater(adviceLayout.flexibleHeight, 0f);
            }
            finally
            {
                if (canvasObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(canvasObject);
                }

                UnityEngine.Object.DestroyImmediate(host);

                EventSystem currentEventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
                if (existingEventSystem == null && currentEventSystem != null)
                {
                    UnityEngine.Object.DestroyImmediate(currentEventSystem.gameObject);
                }
            }
        }

        [Test]
        public void OfficialLocalizationTablesProvideChineseEnglishAndJapanese()
        {
            LocalizationSettings.InitializationOperation.WaitForCompletion();
            Locale originalLocale = LocalizationSettings.SelectedLocale;
            Locale chinese = LocalizationSettings.AvailableLocales.GetLocale(new LocaleIdentifier("zh-Hans"));
            Locale english = LocalizationSettings.AvailableLocales.GetLocale(new LocaleIdentifier("en"));
            Locale japanese = LocalizationSettings.AvailableLocales.GetLocale(new LocaleIdentifier("ja"));

            Assert.NotNull(chinese);
            Assert.NotNull(english);
            Assert.NotNull(japanese);
            Assert.AreEqual(
                "USD/JPY 交易助手",
                LocalizationSettings.StringDatabase.GetLocalizedString(FxTradeLocalization.TableName, "app_title", chinese));
            Assert.AreEqual(
                "USD/JPY Trade Advisor",
                LocalizationSettings.StringDatabase.GetLocalizedString(FxTradeLocalization.TableName, "app_title", english));
            Assert.AreEqual(
                "USD/JPY 取引アドバイザー",
                LocalizationSettings.StringDatabase.GetLocalizedString(FxTradeLocalization.TableName, "app_title", japanese));

            try
            {
                Assert.IsTrue(FxTradeLocalization.TrySelectLocale("en", false));
                Assert.AreEqual("en", FxTradeLocalization.GetSelectedLocaleCode());
                Assert.IsTrue(FxTradeLocalization.TrySelectLocale("ja", false));
                Assert.AreEqual("ja", FxTradeLocalization.GetSelectedLocaleCode());
            }
            finally
            {
                LocalizationSettings.SelectedLocale = originalLocale ?? chinese;
            }
        }
    }
}
