using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TestFXTrade.Fx.UI
{
    [DisallowMultipleComponent]
    public sealed class FxTradeLanguageWindow : MonoBehaviour
    {
        [SerializeField] private TMP_Dropdown languageDropdown;
        [SerializeField] private Button backButton;

        private Action localeChanged;
        private Action back;

        public TMP_Dropdown LanguageDropdown => languageDropdown;

        public void Configure(TMP_Dropdown dropdown, Button returnButton)
        {
            languageDropdown = dropdown;
            backButton = returnButton;
        }

        public void Initialize(Action onLocaleChanged, Action onBack)
        {
            Unbind();
            localeChanged = onLocaleChanged;
            back = onBack;
            languageDropdown.SetValueWithoutNotify(FxTradeLocalization.GetSelectedLocaleIndex());
            languageDropdown.onValueChanged.AddListener(HandleLanguageSelected);
            backButton.onClick.AddListener(HandleBack);
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void HandleLanguageSelected(int index)
        {
            if (FxTradeLocalization.SelectLocaleAt(index))
            {
                localeChanged?.Invoke();
                return;
            }

            languageDropdown.SetValueWithoutNotify(FxTradeLocalization.GetSelectedLocaleIndex());
        }

        private void HandleBack()
        {
            back?.Invoke();
        }

        private void Unbind()
        {
            if (languageDropdown != null)
            {
                languageDropdown.onValueChanged.RemoveListener(HandleLanguageSelected);
            }

            if (backButton != null)
            {
                backButton.onClick.RemoveListener(HandleBack);
            }

            localeChanged = null;
            back = null;
        }
    }
}
