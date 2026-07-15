using System.Reflection;
using NUnit.Framework;
using TestFXTrade.Fx.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TestFXTrade.Tests.EditMode.Fx
{
    public sealed class SmartAdvisorUiTests
    {
        [Test]
        public void PortraitAdvisorHasTwoInputsAndFitsReferenceHeightWithoutPageScroll()
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
                InputField[] accountInputs = canvasObject.GetComponentsInChildren<InputField>(true);
                Assert.AreEqual(2, accountInputs.Length);
                int passwordInputCount = 0;
                for (int i = 0; i < accountInputs.Length; i++)
                {
                    if (accountInputs[i].contentType == InputField.ContentType.Password)
                    {
                        passwordInputCount++;
                    }
                }

                Assert.AreEqual(0, passwordInputCount);
                Button[] buttons = canvasObject.GetComponentsInChildren<Button>(true);
                Assert.AreEqual(4, buttons.Length);

                Transform loadingOverlay = canvasObject.transform.Find("Root/Loading Overlay");
                Assert.NotNull(loadingOverlay);
                Assert.IsFalse(loadingOverlay.gameObject.activeSelf);
                Image loadingBackdrop = loadingOverlay.GetComponent<Image>();
                Assert.NotNull(loadingBackdrop);
                Assert.IsTrue(loadingBackdrop.raycastTarget);
                Assert.NotNull(loadingOverlay.Find("Loading Panel/Loading Spinner"));

                CanvasGroup contentCanvasGroup = canvasObject.transform.Find("Root/Safe Area Content").GetComponent<CanvasGroup>();
                Assert.NotNull(contentCanvasGroup);
                Assert.IsTrue(contentCanvasGroup.interactable);
                Assert.IsTrue(contentCanvasGroup.blocksRaycasts);

                MethodInfo beginLoading = typeof(FxTradeAdvisorApp).GetMethod("BeginLoading", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo endLoading = typeof(FxTradeAdvisorApp).GetMethod("EndLoading", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(beginLoading);
                Assert.NotNull(endLoading);

                beginLoading.Invoke(app, new object[] { "正在测试加载状态" });
                beginLoading.Invoke(app, new object[] { "正在测试嵌套任务" });
                Assert.IsTrue(loadingOverlay.gameObject.activeSelf);
                Assert.IsFalse(contentCanvasGroup.interactable);
                Assert.IsFalse(contentCanvasGroup.blocksRaycasts);

                endLoading.Invoke(app, null);
                Assert.IsTrue(loadingOverlay.gameObject.activeSelf);
                endLoading.Invoke(app, null);
                Assert.IsFalse(loadingOverlay.gameObject.activeSelf);
                Assert.IsTrue(contentCanvasGroup.interactable);
                Assert.IsTrue(contentCanvasGroup.blocksRaycasts);

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
