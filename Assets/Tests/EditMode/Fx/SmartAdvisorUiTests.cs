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

                Transform safeArea = canvasObject.transform.Find("Root/Safe Area Content");
                Assert.NotNull(safeArea);
                Assert.IsNull(safeArea.GetComponent<ScrollRect>());

                RectTransform safeAreaRect = safeArea.GetComponent<RectTransform>();
                float preferredHeight = LayoutUtility.GetPreferredHeight(safeAreaRect);
                Assert.LessOrEqual(preferredHeight, 844f);
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
