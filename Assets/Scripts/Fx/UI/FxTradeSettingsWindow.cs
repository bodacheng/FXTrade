using System;
using UnityEngine;
using UnityEngine.UI;

namespace TestFXTrade.Fx.UI
{
    [DisallowMultipleComponent]
    public sealed class FxTradeSettingsWindow : MonoBehaviour
    {
        [SerializeField] private Button languageSettingsButton;
        [SerializeField] private Button closeButton;

        private Action openLanguageSettings;
        private Action close;

        public void Configure(Button languageButton, Button closeWindowButton)
        {
            languageSettingsButton = languageButton;
            closeButton = closeWindowButton;
        }

        public void Initialize(Action onOpenLanguageSettings, Action onClose)
        {
            Unbind();
            openLanguageSettings = onOpenLanguageSettings;
            close = onClose;
            languageSettingsButton.onClick.AddListener(HandleOpenLanguageSettings);
            closeButton.onClick.AddListener(HandleClose);
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void HandleOpenLanguageSettings()
        {
            openLanguageSettings?.Invoke();
        }

        private void HandleClose()
        {
            close?.Invoke();
        }

        private void Unbind()
        {
            if (languageSettingsButton != null)
            {
                languageSettingsButton.onClick.RemoveListener(HandleOpenLanguageSettings);
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(HandleClose);
            }

            openLanguageSettings = null;
            close = null;
        }
    }
}
