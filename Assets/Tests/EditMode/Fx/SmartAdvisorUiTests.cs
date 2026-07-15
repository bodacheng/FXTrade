using System.Reflection;
using NUnit.Framework;
using TestFXTrade.Fx.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TestFXTrade.Tests.EditMode.Fx
{
    public sealed class SmartAdvisorUiTests
    {
        [Test]
        public void PortraitAdvisorUsesTmpAndNonBlockingLoadingWithinReferenceHeight()
        {
            EventSystem existingEventSystem = Object.FindAnyObjectByType<EventSystem>();
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
                TMP_InputField[] accountInputs = canvasObject.GetComponentsInChildren<TMP_InputField>(true);
                Assert.AreEqual(2, accountInputs.Length);
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
                Assert.AreEqual(1, canvasObject.GetComponentsInChildren<TMP_Dropdown>(true).Length);
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
                MethodInfo tryReadInputs = typeof(FxTradeAdvisorApp).GetMethod("TryReadAdvisorInputs", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(tryReadInputs);
                object[] inputArgs = { 0d, 0d };
                Assert.IsTrue((bool)tryReadInputs.Invoke(app, inputArgs));
                Assert.AreEqual(1000000d, (double)inputArgs[0]);
                Assert.AreEqual(-0.1d, (double)inputArgs[1], 0.0000001d);

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

                beginLoading.Invoke(app, new object[] { "正在测试加载状态" });
                beginLoading.Invoke(app, new object[] { "正在测试嵌套任务" });
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
                    Object.DestroyImmediate(canvasObject);
                }

                Object.DestroyImmediate(host);

                EventSystem currentEventSystem = Object.FindAnyObjectByType<EventSystem>();
                if (existingEventSystem == null && currentEventSystem != null)
                {
                    Object.DestroyImmediate(currentEventSystem.gameObject);
                }
            }
        }
    }
}
