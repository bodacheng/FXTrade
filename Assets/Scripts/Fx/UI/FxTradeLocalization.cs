using System;
using System.Globalization;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Settings;

namespace TestFXTrade.Fx.UI
{
    public static class FxTradeLocalization
    {
        public const string TableName = "FxTradeUi";
        public const string DefaultLocaleCode = "zh-Hans";
        public const string EnglishLocaleCode = "en";
        public const string JapaneseLocaleCode = "ja";

        private const string SavedLocaleKey = "TestFXTrade.SelectedLocale";

        public static readonly string[] SupportedLocaleCodes =
        {
            DefaultLocaleCode,
            EnglishLocaleCode,
            JapaneseLocaleCode
        };

        public static readonly string[] NativeLocaleNames =
        {
            "中文",
            "English",
            "日本語"
        };

        public static async Task ApplySavedLocaleAsync()
        {
            string savedLocale = PlayerPrefs.GetString(SavedLocaleKey, string.Empty);
            if (string.IsNullOrWhiteSpace(savedLocale))
            {
                return;
            }

            try
            {
                var initialization = LocalizationSettings.InitializationOperation;
                if (!initialization.IsDone)
                {
                    await initialization.Task;
                }

                TrySelectLocale(savedLocale, false);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Unable to restore locale '{savedLocale}': {exception.Message}");
            }
        }

        public static bool SelectLocaleAt(int index)
        {
            if (index < 0 || index >= SupportedLocaleCodes.Length)
            {
                return false;
            }

            return TrySelectLocale(SupportedLocaleCodes[index], true);
        }

        public static bool TrySelectLocale(string localeCode, bool persist)
        {
            try
            {
                if (!LocalizationSettings.InitializationOperation.IsDone)
                {
                    Debug.LogWarning(
                        $"Unable to select locale '{localeCode}' before localization initialization completes.");
                    return false;
                }

                Locale locale = LocalizationSettings.AvailableLocales.GetLocale(new LocaleIdentifier(localeCode));
                if (locale == null)
                {
                    return false;
                }

                LocalizationSettings.SelectedLocale = locale;
                if (persist)
                {
                    PlayerPrefs.SetString(SavedLocaleKey, locale.Identifier.Code);
                    PlayerPrefs.Save();
                }

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Unable to select locale '{localeCode}': {exception.Message}");
                return false;
            }
        }

        public static int GetSelectedLocaleIndex()
        {
            string selectedCode = GetSelectedLocaleCode();
            for (int i = 0; i < SupportedLocaleCodes.Length; i++)
            {
                if (string.Equals(SupportedLocaleCodes[i], selectedCode, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return 0;
        }

        public static string GetSelectedLocaleCode()
        {
            try
            {
                if (!LocalizationSettings.InitializationOperation.IsDone)
                {
                    string savedLocale = PlayerPrefs.GetString(SavedLocaleKey, DefaultLocaleCode);
                    return Array.Exists(
                        SupportedLocaleCodes,
                        code => string.Equals(code, savedLocale, StringComparison.OrdinalIgnoreCase))
                        ? savedLocale
                        : DefaultLocaleCode;
                }

                Locale locale = LocalizationSettings.SelectedLocale;
                return locale == null ? DefaultLocaleCode : locale.Identifier.Code;
            }
            catch (Exception)
            {
                return DefaultLocaleCode;
            }
        }

        public static string GetAiResponseLanguageInstruction()
        {
            switch (GetSelectedLocaleCode())
            {
                case EnglishLocaleCode:
                    return "English";
                case JapaneseLocaleCode:
                    return "Japanese";
                default:
                    return "Simplified Chinese";
            }
        }

        public static void Bind(TMP_Text target, string key, string chineseFallback, params object[] arguments)
        {
            if (target == null)
            {
                return;
            }

            target.text = FormatFallback(chineseFallback, arguments);

            LocalizeStringEvent localizer = target.GetComponent<LocalizeStringEvent>();
            if (localizer == null)
            {
                localizer = target.gameObject.AddComponent<LocalizeStringEvent>();
            }

            localizer.OnUpdateString.RemoveAllListeners();
            localizer.OnUpdateString.AddListener(value =>
            {
                if (target != null && !string.IsNullOrEmpty(value) && !string.Equals(value, key, StringComparison.Ordinal))
                {
                    target.text = value;
                }
            });

            LocalizedString reference = new LocalizedString(TableName, key)
            {
                Arguments = arguments
            };
            localizer.StringReference = reference;
            localizer.RefreshString();
        }

        public static string Get(string key, string chineseFallback, params object[] arguments)
        {
            string fallback = FormatFallback(chineseFallback, arguments);
            try
            {
                if (!LocalizationSettings.InitializationOperation.IsDone)
                {
                    return fallback;
                }

                var operation = LocalizationSettings.StringDatabase.GetLocalizedStringAsync(
                    TableName,
                    key,
                    LocalizationSettings.SelectedLocale,
                    FallbackBehavior.UseProjectSettings,
                    arguments);
                if (!operation.IsDone || operation.Status != UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
                {
                    return fallback;
                }

                string localized = operation.Result;
                return string.IsNullOrEmpty(localized) || string.Equals(localized, key, StringComparison.Ordinal)
                    ? fallback
                    : localized;
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private static string FormatFallback(string fallback, object[] arguments)
        {
            if (string.IsNullOrEmpty(fallback) || arguments == null || arguments.Length == 0)
            {
                return fallback ?? string.Empty;
            }

            try
            {
                return string.Format(CultureInfo.InvariantCulture, fallback, arguments);
            }
            catch (FormatException)
            {
                return fallback;
            }
        }
    }
}
