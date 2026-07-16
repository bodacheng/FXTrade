using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TestFXTrade.Fx.Domain;
using TestFXTrade.Fx.OpenAI;
using TestFXTrade.Fx.Sbi;
using TestFXTrade.Fx.UI;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TestFXTrade.Tests.EditMode.Fx
{
    public sealed class SmartAdvisorUiTests
    {
        [Test]
        public void AdvisorPageAndAddressableWindowsAreSeparatedFromSampleScene()
        {
            GameObject pagePrefab = Resources.Load<GameObject>("Pages/FxTradeAdvisorPage");
            Assert.NotNull(pagePrefab);
            Assert.AreEqual("FxTradeAdvisorPage", pagePrefab.name);
            Assert.NotNull(pagePrefab.GetComponent<Canvas>());
            Assert.NotNull(pagePrefab.GetComponent<CanvasScaler>());
            Assert.NotNull(pagePrefab.GetComponent<GraphicRaycaster>());

            FxTradeAdvisorApp app = pagePrefab.GetComponent<FxTradeAdvisorApp>();
            Assert.NotNull(app);
            MethodInfo hasCompleteReferences = typeof(FxTradeAdvisorApp).GetMethod(
                "HasCompleteSceneUiReferences",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(hasCompleteReferences);
            Assert.IsTrue((bool)hasCompleteReferences.Invoke(app, null));

            Assert.NotNull(pagePrefab.transform.Find("Root/Safe Area Content"));
            Assert.NotNull(pagePrefab.transform.Find("Root/Safe Area Content/Market Overview Card"));
            Assert.NotNull(pagePrefab.transform.Find("Root/Safe Area Content/Advisor Setup Card"));
            Assert.NotNull(pagePrefab.transform.Find("Root/Safe Area Content/Market Insight Card/ChartPanel/ChartLine"));
            Assert.NotNull(pagePrefab.transform.Find("Root/Safe Area Content/Advice Access Card/Advice Result Button"));
            Assert.IsNull(pagePrefab.transform.Find("Root/Safe Area Content/Advice Card"));
            Assert.NotNull(pagePrefab.transform.Find("Root/Safe Area Content/Warning Card"));
            Assert.NotNull(pagePrefab.transform.Find("Root/Safe Area Content/Header Card/Header Bar/Header Actions/Loading Indicator"));
            Transform prefabSettingsButton = pagePrefab.transform.Find(
                "Root/Safe Area Content/Header Card/Header Bar/Header Actions/Settings Button");
            Assert.NotNull(prefabSettingsButton);
            Transform prefabSettingsIconTransform = prefabSettingsButton.Find("Settings Icon");
            Assert.NotNull(prefabSettingsIconTransform);
            Image prefabSettingsIcon = prefabSettingsIconTransform.GetComponent<Image>();
            Assert.NotNull(prefabSettingsIcon);
            Assert.NotNull(prefabSettingsIcon.sprite);
            Assert.AreEqual("setting_17909217_0", prefabSettingsIcon.sprite.name);
            Transform prefabUsageGuideButton = pagePrefab.transform.Find(
                "Root/Safe Area Content/Header Card/Header Bar/Header Actions/Usage Guide Button");
            Assert.NotNull(prefabUsageGuideButton);
            Transform prefabUsageGuideIconTransform = prefabUsageGuideButton.Find("Usage Guide Icon");
            Assert.NotNull(prefabUsageGuideIconTransform);
            Image prefabUsageGuideIcon = prefabUsageGuideIconTransform.GetComponent<Image>();
            Assert.NotNull(prefabUsageGuideIcon);
            Assert.NotNull(prefabUsageGuideIcon.sprite);
            Assert.That(prefabUsageGuideIcon.sprite.name, Does.StartWith("question-mark-symbol-isolated"));
            Assert.IsNull(pagePrefab.transform.Find("Root/Safe Area Content/Language Controls"));
            Assert.IsNull(pagePrefab.transform.Find("Root/Settings Window"));
            Assert.IsNull(pagePrefab.transform.Find("Root/Language Window"));
            Assert.IsNull(pagePrefab.transform.Find("Root/Usage Guide Window"));
            Assert.IsNull(pagePrefab.transform.Find("Root/Advice Window"));

            AssetReferenceGameObject settingsReference = GetAssetReference(app, "settingsWindowPrefab");
            AssetReferenceGameObject languageReference = GetAssetReference(app, "languageWindowPrefab");
            AssetReferenceGameObject guideReference = GetAssetReference(app, "usageGuideWindowPrefab");
            AssetReferenceGameObject adviceReference = GetAssetReference(app, "adviceWindowPrefab");
            Assert.AreEqual(
                "Assets/UI/Addressable/FxTradeSettingsWindow.prefab",
                AssetDatabase.GUIDToAssetPath(settingsReference.AssetGUID));
            Assert.AreEqual(
                "Assets/UI/Addressable/FxTradeLanguageWindow.prefab",
                AssetDatabase.GUIDToAssetPath(languageReference.AssetGUID));
            Assert.AreEqual(
                "Assets/UI/Addressable/FxTradeUsageGuideWindow.prefab",
                AssetDatabase.GUIDToAssetPath(guideReference.AssetGUID));
            Assert.AreEqual(
                "Assets/UI/Addressable/FxTradeAdviceWindow.prefab",
                AssetDatabase.GUIDToAssetPath(adviceReference.AssetGUID));

            GameObject settingsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/UI/Addressable/FxTradeSettingsWindow.prefab");
            GameObject languagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/UI/Addressable/FxTradeLanguageWindow.prefab");
            GameObject guidePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/UI/Addressable/FxTradeUsageGuideWindow.prefab");
            GameObject advicePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/UI/Addressable/FxTradeAdviceWindow.prefab");
            Assert.NotNull(settingsPrefab);
            Assert.NotNull(languagePrefab);
            Assert.NotNull(guidePrefab);
            Assert.NotNull(advicePrefab);
            Assert.NotNull(settingsPrefab.GetComponent<FxTradeSettingsWindow>());
            Assert.NotNull(settingsPrefab.transform.Find("Settings Panel/Language Settings Button"));
            Assert.IsNull(settingsPrefab.transform.Find("Settings Panel/Usage Guide Button"));
            Assert.NotNull(settingsPrefab.transform.Find("Settings Panel/Close Settings Button"));
            Assert.NotNull(languagePrefab.GetComponent<FxTradeLanguageWindow>());
            Assert.NotNull(languagePrefab.transform.Find("Language Panel/Language Settings/语言 Field/语言 Dropdown"));
            Assert.NotNull(languagePrefab.transform.Find("Language Panel/Back Button"));
            Assert.NotNull(guidePrefab.GetComponent<FxTradeUsageGuideWindow>());
            Assert.NotNull(guidePrefab.transform.Find("Usage Guide Page/Usage Guide Header/Back Button"));
            Transform usageGuideScrollView = guidePrefab.transform.Find("Usage Guide Page/Usage Guide Scroll View");
            Assert.NotNull(usageGuideScrollView);
            Assert.NotNull(usageGuideScrollView.GetComponent<ScrollRect>());
            Transform usageGuideBody = usageGuideScrollView.Find("Viewport/Content/Usage Guide Body");
            Assert.NotNull(usageGuideBody);
            Assert.That(usageGuideBody.GetComponent<TMP_Text>().text, Does.Contain("AzureRelayConfig.json"));
            Assert.NotNull(advicePrefab.GetComponent<FxTradeAdviceWindow>());
            Assert.NotNull(advicePrefab.transform.Find("Advice Page/Advice Header/Close Button"));
            Transform adviceScrollView = advicePrefab.transform.Find("Advice Page/Advice Scroll View");
            Assert.NotNull(adviceScrollView);
            Assert.NotNull(adviceScrollView.GetComponent<ScrollRect>());
            Assert.NotNull(adviceScrollView.Find("Viewport/Content/Advice Body"));

            AddressableAssetSettings addressableSettings = AddressableAssetSettingsDefaultObject.Settings;
            AddressableAssetGroup uiGroup = addressableSettings.FindGroup("FX Trade UI");
            Assert.NotNull(uiGroup);
            AssertAddressableEntry(addressableSettings, settingsReference, FxTradeAdvisorApp.SettingsWindowAddress);
            AssertAddressableEntry(addressableSettings, languageReference, FxTradeAdvisorApp.LanguageWindowAddress);
            AssertAddressableEntry(addressableSettings, guideReference, FxTradeAdvisorApp.UsageGuideWindowAddress);
            AssertAddressableEntry(addressableSettings, adviceReference, FxTradeAdvisorApp.AdviceWindowAddress);

            Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);

            try
            {
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    Assert.IsNull(roots[i].GetComponentInChildren<FxTradeAdvisorApp>(true));
                    Assert.IsNull(roots[i].GetComponentInChildren<EventSystem>(true));
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static AssetReferenceGameObject GetAssetReference(FxTradeAdvisorApp app, string fieldName)
        {
            FieldInfo field = typeof(FxTradeAdvisorApp).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            AssetReferenceGameObject reference = field.GetValue(app) as AssetReferenceGameObject;
            Assert.NotNull(reference);
            Assert.IsTrue(reference.RuntimeKeyIsValid());
            return reference;
        }

        private static void AssertAddressableEntry(
            AddressableAssetSettings settings,
            AssetReferenceGameObject reference,
            string expectedAddress)
        {
            AddressableAssetEntry entry = settings.FindAssetEntry(reference.AssetGUID);
            Assert.NotNull(entry);
            Assert.AreEqual("FX Trade UI", entry.parentGroup.Name);
            Assert.AreEqual(expectedAddress, entry.address);
            Assert.Contains("FXTradeUI", new List<string>(entry.labels));
        }

        [Test]
        public void BootstrapInstantiatesAdvisorPageResource()
        {
            EventSystem existingEventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            FxTradeAdvisorApp pageApp = null;
            FxTradeAdvisorApp existingPageApp = UnityEngine.Object.FindAnyObjectByType<FxTradeAdvisorApp>();

            try
            {
                if (existingPageApp != null)
                {
                    UnityEngine.Object.DestroyImmediate(existingPageApp.gameObject);
                }

                MethodInfo bootstrap = typeof(FxTradeAdvisorApp).GetMethod(
                    "Bootstrap",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.NotNull(bootstrap);

                bootstrap.Invoke(null, null);
                pageApp = UnityEngine.Object.FindAnyObjectByType<FxTradeAdvisorApp>();
                Assert.NotNull(pageApp);
                Assert.AreEqual("FxTradeAdvisorPage", pageApp.gameObject.name);
                Assert.NotNull(pageApp.GetComponent<Canvas>());
            }
            finally
            {
                if (pageApp != null)
                {
                    UnityEngine.Object.DestroyImmediate(pageApp.gameObject);
                }

                EventSystem currentEventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
                if (existingEventSystem == null && currentEventSystem != null)
                {
                    UnityEngine.Object.DestroyImmediate(currentEventSystem.gameObject);
                }
            }
        }

        [Test]
        public void AddressableWindowEntriesAndControllersAreIndependent()
        {
            LocalizationSettings.InitializationOperation.WaitForCompletion();
            GameObject settingsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/UI/Addressable/FxTradeSettingsWindow.prefab");
            GameObject languagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/UI/Addressable/FxTradeLanguageWindow.prefab");
            GameObject guidePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/UI/Addressable/FxTradeUsageGuideWindow.prefab");
            GameObject advicePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/UI/Addressable/FxTradeAdviceWindow.prefab");

            GameObject settingsInstance = UnityEngine.Object.Instantiate(settingsPrefab);
            bool languageRequested = false;
            bool closeRequested = false;
            FxTradeSettingsWindow settingsWindow = settingsInstance.GetComponent<FxTradeSettingsWindow>();
            settingsWindow.Initialize(
                () => languageRequested = true,
                () => closeRequested = true);
            settingsInstance.transform.Find("Settings Panel/Language Settings Button").GetComponent<Button>().onClick.Invoke();
            settingsInstance.transform.Find("Settings Panel/Close Settings Button").GetComponent<Button>().onClick.Invoke();
            Assert.IsTrue(languageRequested);
            Assert.IsTrue(closeRequested);
            UnityEngine.Object.DestroyImmediate(settingsInstance);
            Assert.IsTrue(settingsInstance == null);

            GameObject languageInstance = UnityEngine.Object.Instantiate(languagePrefab);
            bool languageBackRequested = false;
            FxTradeLanguageWindow languageWindow = languageInstance.GetComponent<FxTradeLanguageWindow>();
            languageWindow.Initialize(null, () => languageBackRequested = true);
            Assert.AreEqual(3, languageWindow.LanguageDropdown.options.Count);
            languageInstance.transform.Find("Language Panel/Back Button").GetComponent<Button>().onClick.Invoke();
            Assert.IsTrue(languageBackRequested);
            UnityEngine.Object.DestroyImmediate(languageInstance);
            Assert.IsTrue(languageInstance == null);

            GameObject guideInstance = UnityEngine.Object.Instantiate(guidePrefab);
            bool guideBackRequested = false;
            FxTradeUsageGuideWindow guideWindow = guideInstance.GetComponent<FxTradeUsageGuideWindow>();
            guideWindow.Initialize(() => guideBackRequested = true);
            Assert.That(guideWindow.BodyText.text, Does.Contain("AzureRelayConfig.json"));
            guideInstance.transform.Find("Usage Guide Page/Usage Guide Header/Back Button").GetComponent<Button>().onClick.Invoke();
            Assert.IsTrue(guideBackRequested);
            UnityEngine.Object.DestroyImmediate(guideInstance);
            Assert.IsTrue(guideInstance == null);

            GameObject adviceInstance = UnityEngine.Object.Instantiate(advicePrefab);
            bool adviceCloseRequested = false;
            FxTradeAdviceWindow adviceWindow = adviceInstance.GetComponent<FxTradeAdviceWindow>();
            adviceWindow.Initialize(
                "测试建议正文",
                "稳健建议 · 生成于 2026-07-16 18:42",
                () => adviceCloseRequested = true);
            Assert.AreEqual("测试建议正文", adviceWindow.BodyText.text);
            Assert.That(adviceWindow.GeneratedAtText.text, Does.Contain("2026-07-16 18:42"));
            adviceInstance.transform.Find("Advice Page/Advice Header/Close Button").GetComponent<Button>().onClick.Invoke();
            Assert.IsTrue(adviceCloseRequested);
            UnityEngine.Object.DestroyImmediate(adviceInstance);
            Assert.IsTrue(adviceInstance == null);
        }

        [Test]
        public void PortraitAdvisorUsesTmpAndNonBlockingLoadingWithinReferenceHeight()
        {
            LocalizationSettings.InitializationOperation.WaitForCompletion();
            Locale originalLocale = LocalizationSettings.SelectedLocale;
            EventSystem existingEventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            GameObject host = new GameObject("UI Test Host");
            GameObject canvasObject = null;

            try
            {
                FxTradeAdvisorApp app = host.AddComponent<FxTradeAdvisorApp>();
                MethodInfo awake = typeof(FxTradeAdvisorApp).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(awake);
                awake.Invoke(app, null);
                Assert.IsTrue(FxTradeLocalization.TrySelectLocale("zh-Hans", false));
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
                Assert.AreEqual(1, canvasObject.GetComponentsInChildren<TMP_Dropdown>(true).Length);
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
                Assert.AreEqual(7, buttons.Length);

                Transform safeArea = canvasObject.transform.Find("Root/Safe Area Content");
                Assert.NotNull(safeArea);
                Assert.IsNull(safeArea.Find("Language Controls"));
                Transform settingsButton = safeArea.Find("Header Card/Header Bar/Header Actions/Settings Button");
                Transform usageGuideButton = safeArea.Find("Header Card/Header Bar/Header Actions/Usage Guide Button");
                Assert.NotNull(settingsButton);
                Assert.NotNull(usageGuideButton);
                Assert.IsNull(canvasObject.transform.Find("Root/Settings Window"));
                Assert.IsNull(canvasObject.transform.Find("Root/Language Window"));
                Assert.IsNull(canvasObject.transform.Find("Root/Usage Guide Window"));
                Assert.IsNull(canvasObject.transform.Find("Root/Advice Window"));
                Transform adviceResultButton = safeArea.Find("Advice Access Card/Advice Result Button");
                Assert.NotNull(adviceResultButton);
                Assert.IsFalse(adviceResultButton.GetComponent<Button>().interactable);
                Assert.That(
                    adviceResultButton.Find("Advice Result Button Text").GetComponent<TMP_Text>().text,
                    Does.Contain("暂无"));

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

                MethodInfo formatAdviceAgeLabel = typeof(FxTradeAdvisorApp).GetMethod(
                    "FormatAdviceAgeLabel",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.NotNull(formatAdviceAgeLabel);
                DateTime generatedAt = new DateTime(2026, 7, 16, 18, 0, 0);
                Assert.AreEqual(
                    "查看建议 · 刚刚",
                    formatAdviceAgeLabel.Invoke(null, new object[] { generatedAt, generatedAt.AddSeconds(20) }));
                Assert.AreEqual(
                    "查看建议 · 3 分钟前",
                    formatAdviceAgeLabel.Invoke(null, new object[] { generatedAt, generatedAt.AddMinutes(3) }));
                Assert.AreEqual(
                    "查看建议 · 2 小时前",
                    formatAdviceAgeLabel.Invoke(null, new object[] { generatedAt, generatedAt.AddHours(2) }));

                FieldInfo rulesField = typeof(FxTradeAdvisorApp).GetField(
                    "sbiRules",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo generatedAtField = typeof(FxTradeAdvisorApp).GetField(
                    "lastAdviceGeneratedAt",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(rulesField);
                Assert.NotNull(generatedAtField);
                rulesField.SetValue(
                    app,
                    new SbiFxRuleSnapshot
                    {
                        SourceUrl = "https://example.test/sbi",
                        Pair = "USD/JPY",
                        Leverage = 25,
                        MarginRatePercent = 4d,
                        RequiredMarginPer10000Jpy = 64000,
                        MinimumOrderUnits = 1000
                    });
                generatedAtField.SetValue(app, DateTime.Now);
                MethodInfo renderAiAdvice = typeof(FxTradeAdvisorApp).GetMethod(
                    "RenderAiAdvice",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(renderAiAdvice);
                renderAiAdvice.Invoke(
                    app,
                    new object[]
                    {
                        new OpenAiTradeAdvice
                        {
                            action = "BUY",
                            suggested_lots = 0.01d,
                            confidence = 0.78d,
                            summary = "短期趋势转强",
                            reasoning = "价格站上短期均线",
                            risk_warning = "注意回撤风险"
                        },
                        1000000d,
                        0d,
                        false,
                        AiTradeAdviceMode.Conservative
                    });
                Assert.IsTrue(adviceResultButton.GetComponent<Button>().interactable);
                Assert.That(
                    adviceResultButton.Find("Advice Result Button Text").GetComponent<TMP_Text>().text,
                    Does.Contain("刚刚"));
                FieldInfo latestAdviceDisplayTextField = typeof(FxTradeAdvisorApp).GetField(
                    "latestAdviceDisplayText",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(latestAdviceDisplayTextField);
                string latestAdviceDisplayText = (string)latestAdviceDisplayTextField.GetValue(app);
                Assert.That(latestAdviceDisplayText, Does.Contain("短期趋势转强"));
                Assert.That(latestAdviceDisplayText, Does.Contain("风险提示"));

                Transform loadingIndicator = canvasObject.transform.Find(
                    "Root/Safe Area Content/Header Card/Header Bar/Header Actions/Loading Indicator");
                Assert.NotNull(loadingIndicator);
                Assert.IsFalse(loadingIndicator.gameObject.activeSelf);
                Image indicatorBackground = loadingIndicator.GetComponent<Image>();
                Assert.NotNull(indicatorBackground);
                Assert.IsFalse(indicatorBackground.raycastTarget);
                Assert.NotNull(loadingIndicator.Find("Loading Spinner"));
                RectTransform indicatorRect = loadingIndicator.GetComponent<RectTransform>();
                Assert.LessOrEqual(indicatorRect.sizeDelta.x, 40f);
                Assert.LessOrEqual(indicatorRect.sizeDelta.y, 40f);
                Assert.IsFalse(loadingIndicator.GetComponent<LayoutElement>().ignoreLayout);

                MethodInfo beginLoading = typeof(FxTradeAdvisorApp).GetMethod("BeginLoading", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo endLoading = typeof(FxTradeAdvisorApp).GetMethod("EndLoading", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(beginLoading);
                Assert.NotNull(endLoading);

                beginLoading.Invoke(app, new object[] { "status_processing", "正在测试加载状态", Array.Empty<object>() });
                beginLoading.Invoke(app, new object[] { "status_processing", "正在测试嵌套任务", Array.Empty<object>() });
                Assert.IsTrue(loadingIndicator.gameObject.activeSelf);
                Canvas.ForceUpdateCanvases();
                RectTransform loadingRect = loadingIndicator.GetComponent<RectTransform>();
                RectTransform usageGuideRect = usageGuideButton.GetComponent<RectTransform>();
                RectTransform settingsRect = settingsButton.GetComponent<RectTransform>();
                Vector3[] loadingCorners = new Vector3[4];
                Vector3[] usageGuideCorners = new Vector3[4];
                Vector3[] settingsCorners = new Vector3[4];
                loadingRect.GetWorldCorners(loadingCorners);
                usageGuideRect.GetWorldCorners(usageGuideCorners);
                settingsRect.GetWorldCorners(settingsCorners);
                Assert.LessOrEqual(loadingCorners[2].x, usageGuideCorners[0].x);
                Assert.LessOrEqual(usageGuideCorners[2].x, settingsCorners[0].x);
                for (int i = 0; i < buttons.Length; i++)
                {
                    Assert.IsTrue(buttons[i].interactable);
                }

                endLoading.Invoke(app, null);
                Assert.IsTrue(loadingIndicator.gameObject.activeSelf);
                endLoading.Invoke(app, null);
                Assert.IsFalse(loadingIndicator.gameObject.activeSelf);

                Assert.IsNull(safeArea.GetComponent<ScrollRect>());

                RectTransform safeAreaRect = safeArea.GetComponent<RectTransform>();
                float preferredHeight = LayoutUtility.GetPreferredHeight(safeAreaRect);
                Assert.LessOrEqual(preferredHeight, 844f);

                Transform chartPanel = safeArea.Find("Market Insight Card/ChartPanel");
                Assert.NotNull(chartPanel);
                Assert.IsNull(safeArea.Find("Advice Card"));

                LayoutElement chartLayout = chartPanel.GetComponent<LayoutElement>();
                Assert.GreaterOrEqual(chartLayout.preferredHeight, 190f);
                Assert.AreEqual(0f, chartLayout.flexibleHeight);
            }
            finally
            {
                LocalizationSettings.SelectedLocale = originalLocale;

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
            Assert.AreEqual(
                "Settings",
                LocalizationSettings.StringDatabase.GetLocalizedString(FxTradeLocalization.TableName, "button_settings", english));
            Assert.AreEqual(
                "閉じる",
                LocalizationSettings.StringDatabase.GetLocalizedString(FxTradeLocalization.TableName, "button_close", japanese));
            Assert.AreEqual(
                "User Guide",
                LocalizationSettings.StringDatabase.GetLocalizedString(FxTradeLocalization.TableName, "button_usage_guide", english));
            Assert.AreEqual(
                "Language Settings",
                LocalizationSettings.StringDatabase.GetLocalizedString(FxTradeLocalization.TableName, "button_language_settings", english));
            Assert.AreEqual(
                "AI Trade Advice",
                LocalizationSettings.StringDatabase.GetLocalizedString(FxTradeLocalization.TableName, "advice_window_title", english));
            Assert.AreEqual(
                "AI取引提案",
                LocalizationSettings.StringDatabase.GetLocalizedString(FxTradeLocalization.TableName, "advice_window_title", japanese));
            Assert.AreEqual(
                "暂无 AI 建议",
                LocalizationSettings.StringDatabase.GetLocalizedString(FxTradeLocalization.TableName, "advice_button_empty", chinese));
            Assert.AreEqual(
                "戻る",
                LocalizationSettings.StringDatabase.GetLocalizedString(FxTradeLocalization.TableName, "button_back", japanese));
            Assert.That(
                LocalizationSettings.StringDatabase.GetLocalizedString(FxTradeLocalization.TableName, "usage_guide_body", english),
                Does.Contain("AzureRelayConfig.json"));

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
