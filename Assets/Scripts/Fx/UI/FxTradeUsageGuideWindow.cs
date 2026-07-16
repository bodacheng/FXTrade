using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TestFXTrade.Fx.UI
{
    [DisallowMultipleComponent]
    public sealed class FxTradeUsageGuideWindow : MonoBehaviour
    {
        [SerializeField] private Button backButton;
        [SerializeField] private TMP_Text bodyText;

        private Action back;

        public TMP_Text BodyText => bodyText;

        public void Configure(Button returnButton, TMP_Text guideBodyText)
        {
            backButton = returnButton;
            bodyText = guideBodyText;
        }

        public void Initialize(Action onBack)
        {
            Unbind();
            back = onBack;
            backButton.onClick.AddListener(HandleBack);
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void HandleBack()
        {
            back?.Invoke();
        }

        private void Unbind()
        {
            if (backButton != null)
            {
                backButton.onClick.RemoveListener(HandleBack);
            }

            back = null;
        }
    }
}
