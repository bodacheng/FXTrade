using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TestFXTrade.Fx.UI
{
    [DisallowMultipleComponent]
    public sealed class FxTradeAdviceWindow : MonoBehaviour
    {
        [SerializeField] private Button closeButton;
        [SerializeField] private TMP_Text generatedAtText;
        [SerializeField] private TMP_Text bodyText;

        private Action close;

        public TMP_Text GeneratedAtText => generatedAtText;
        public TMP_Text BodyText => bodyText;

        public void Configure(Button closeWindowButton, TMP_Text adviceGeneratedAtText, TMP_Text adviceBodyText)
        {
            closeButton = closeWindowButton;
            generatedAtText = adviceGeneratedAtText;
            bodyText = adviceBodyText;
        }

        public void Initialize(string adviceBody, string generatedAt, Action onClose)
        {
            Unbind();
            bodyText.text = adviceBody ?? string.Empty;
            generatedAtText.text = generatedAt ?? string.Empty;
            close = onClose;
            closeButton.onClick.AddListener(HandleClose);
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void HandleClose()
        {
            close?.Invoke();
        }

        private void Unbind()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(HandleClose);
            }

            close = null;
        }
    }
}
