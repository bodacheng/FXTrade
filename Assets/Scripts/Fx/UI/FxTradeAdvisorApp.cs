using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TestFXTrade.Fx.Analysis;
using TestFXTrade.Fx.Domain;
using TestFXTrade.Fx.MarketData;
using TestFXTrade.Fx.OpenAI;
using TestFXTrade.Fx.Sbi;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace TestFXTrade.Fx.UI
{
    [DefaultExecutionOrder(-500)]
    public sealed class FxTradeAdvisorApp : MonoBehaviour
    {
        private const float QuoteRefreshSeconds = 5f;
        private const float CandleRefreshSeconds = 60f;
        private const float AdviceAgeRefreshSeconds = 15f;
        private const int CandleOutputSize = 160;
        private const int DynamicFontSize = 90;
        private const string PageResourcePath = "Pages/FxTradeAdvisorPage";
        private const string StartupCurtainName = "Startup Curtain";
        private const string AppTitlePath =
            "Root/Safe Area Content/Header Card/Header Bar/Brand Block/Text";
        public const string SettingsWindowAddress = "TestFXTrade/UI/SettingsWindow";
        public const string LanguageWindowAddress = "TestFXTrade/UI/LanguageWindow";
        public const string UsageGuideWindowAddress = "TestFXTrade/UI/UsageGuideWindow";
        public const string AdviceWindowAddress = "TestFXTrade/UI/AdviceWindow";
        private const string BundledChineseFontResourcePath = "Fonts/NotoSansSC-Regular";
        private const string ChineseFontProbeText = "中文交易建议保证金行情规则买卖建玉数量通貨同步均价建仓";
        private const string UsageGuideFallback =
            "使用方法\n\n" +
            "1. 打开主画面，确认 USD/JPY 行情已经更新。可选择 1min、5min、15min 周期，或点击“刷新”手动更新。\n\n" +
            "2. 输入用于估算的本金（JPY）、当前净建玉数量（通貨）和 USD/JPY 建仓均价。数量为正表示买入持仓，为负表示卖出持仓，0 表示空仓。App 不会读取 SBI 账户，请按实际情况手动填写。\n\n" +
            "3. 点击“SBI保证金 / 更新”。App 会从 SBI 证券官方页面取得 USD/JPY 当前适用的杠杆倍数、保证金率、每 1 万通貨必要保证金、最小交易单位和适用日期，并保存在本机。这只是更新计算规则，不会同步账户、持仓或订单，也不会执行交易。\n\n" +
            "4. 更新后的规则会与本金、持仓及行情一起用于 AI 分析。App 还会按 SBI 的必要保证金和最小交易单位重新校验建议；稳健建议把预计保证金控制在本金的 50% 以内，积极建议为 70% 以内。超出限制时，建议数量可能被缩小、方向可能被调整，或改为观望。SBI 调整保证金后，请再次更新。\n\n" +
            "5. 选择“稳健建议”或“积极建议”。稳健模式在信号不足时可以建议观望；积极模式会要求 AI 在买入和卖出中选择方向，但这不代表信号充分。\n\n" +
            "6. 查看图表、技术指标、AI 建议和风险提示，再自行决定是否交易。\n\n" +
            "注意事项\n\n" +
            "• 行情每 5 秒自动更新，K线约每 60 秒刷新。\n" +
            "• App 不连接 SBI 账户，所有本金和持仓数据均由用户输入。\n" +
            "• AI 建议仅供参考，不构成投资建议，也不保证符合 SBI 的实际交易审查结果。\n" +
            "• 无法获取行情、规则或建议时，请检查网络连接后重试。";
        private const NumberStyles UserNumberStyles = NumberStyles.Float | NumberStyles.AllowThousands;
        private static readonly Vector2 MobileReferenceResolution = new Vector2(390f, 844f);
        private static readonly Regex VisibleLotQuantityPattern = new Regex(
            @"(?<value>[+-]?\d+(?:\.\d+)?)\s*(?:standard\s*)?lots?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex VisibleLotWordPattern = new Regex(
            @"\blots?\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly string[,] ChineseSystemFontCandidates =
        {
            { "PingFang SC", null },
            { "PingFang SC", "Medium" },
            { "Heiti SC", null },
            { "STHeiti", null },
            { "Hiragino Sans GB", null },
            { "Noto Sans CJK SC", null },
            { "Noto Sans SC", null },
            { "Microsoft YaHei", null },
            { "Droid Sans Fallback", null },
            { "Arial Unicode MS", null }
        };

        private TMP_FontAsset font;

        [Header("Page Prefab UI")]
        [SerializeField] private Canvas uiCanvas;
        [SerializeField] private CanvasScaler canvasScaler;
        [SerializeField] private RectTransform safeAreaContentRect;
        [SerializeField] private TMP_InputField principalInput;
        [SerializeField] private TMP_InputField netPositionInput;
        [SerializeField] private TMP_InputField positionEntryPriceInput;
        [SerializeField] private TMP_Dropdown intervalDropdown;
        [SerializeField] private Sprite settingsIcon;
        [SerializeField] private Sprite usageGuideIcon;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button usageGuideToolbarButton;
        [Header("Addressable UI Windows")]
        [SerializeField] private AssetReferenceGameObject settingsWindowPrefab;
        [SerializeField] private AssetReferenceGameObject languageWindowPrefab;
        [SerializeField] private AssetReferenceGameObject usageGuideWindowPrefab;
        [SerializeField] private AssetReferenceGameObject adviceWindowPrefab;
        [SerializeField] private Toggle autoRefreshToggle;
        [SerializeField] private Button refreshMarketButton;
        [SerializeField] private Button syncSbiRulesButton;
        [SerializeField] private Button requestAiAdviceButton;
        [SerializeField] private Button aggressiveAiAdviceButton;
        [SerializeField] private Button reopenAdviceButton;
        [SerializeField] private TMP_Text reopenAdviceButtonText;
        [SerializeField] private TMP_Text marketSourceText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text sbiRulesText;
        [SerializeField] private TMP_Text quoteText;
        [SerializeField] private TMP_Text metricsText;
        [SerializeField] private TMP_Text warningsText;
        [SerializeField] private UsdJpyTrendLineGraphic chartGraphic;
        [SerializeField] private GameObject loadingIndicator;
        [SerializeField] private RectTransform loadingSpinnerRect;
        private Rect lastSafeArea = new Rect(-1f, -1f, -1f, -1f);
        private Vector2Int lastScreenSize = new Vector2Int(-1, -1);

        private readonly SbiFxRuleService sbiRuleService = new SbiFxRuleService();
        private readonly List<Candle> latestCandles = new List<Candle>();
        private CancellationTokenSource marketDataCancellation;
        private CancellationTokenSource advisoryCancellation;
        private IFxMarketDataProvider marketDataProvider;
        private AzureRelayTradeAdvisorClient openAiClient;
        private SbiFxRuleSnapshot sbiRules;
        private MarketQuote latestQuote;
        private string relayBaseUrl = string.Empty;
        private OpenAiTradeAdvice lastAdvice;
        private double lastAdvicePrincipalJpy;
        private double lastAdviceCurrentNetLots;
        private bool lastAdviceAdjusted;
        private AiTradeAdviceMode lastAdviceMode;
        private string latestAdviceDisplayText = string.Empty;
        private DateTime? lastAdviceGeneratedAt;
        private float nextQuoteRefreshAt;
        private float nextCandleRefreshAt;
        private float nextAdviceAgeRefreshAt;
        private bool quoteRefreshInFlight;
        private bool candleRefreshInFlight;
        private bool sbiSyncInFlight;
        private bool aiAdviceInFlight;
        private int loadingOperationCount;
        private GameObject startupCurtain;
        private GameObject activeAddressableWindow;
        private RectTransform activeAddressableWindowSafeAreaRect;
        private int addressableWindowRequestVersion;

        private enum AddressableWindowKind
        {
            Settings,
            Language,
            UsageGuide,
            Advice
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<FxTradeAdvisorApp>() != null)
            {
                return;
            }

            GameObject pagePrefab = Resources.Load<GameObject>(PageResourcePath);
            if (pagePrefab != null)
            {
                GameObject page = UnityEngine.Object.Instantiate(pagePrefab);
                page.name = pagePrefab.name;
                if (page.GetComponent<FxTradeAdvisorApp>() != null)
                {
                    return;
                }

                Debug.LogError($"The page resource '{PageResourcePath}' does not contain FxTradeAdvisorApp.");
                UnityEngine.Object.Destroy(page);
            }
            else
            {
                Debug.LogError($"Could not load the UI page resource '{PageResourcePath}'.");
            }

            GameObject fallbackApp = new GameObject("USDJPY Trade Advisor App");
            fallbackApp.AddComponent<FxTradeAdvisorApp>();
        }

        private void Awake()
        {
            ConfigurePortraitRuntime();
            ShowStartupCurtain();
            font = CreateChineseUiFont();

            if (uiCanvas == null)
            {
                BuildUi();
                ShowStartupCurtain();
                return;
            }

            if (!HasCompleteSceneUiReferences())
            {
                Debug.LogError(
                    "The page prefab is missing one or more FxTradeAdvisorApp UI references. " +
                    "Rebuild it from Tools/FX Trade/Rebuild Advisor Page Prefab.",
                    this);
                enabled = false;
                return;
            }

            PrepareBoundSceneUi();
        }

        private async void Start()
        {
            await FxTradeLocalization.ApplySavedLocaleAsync();
            if (this == null || !enabled || uiCanvas == null)
            {
                return;
            }

            FxTradeLocalization.RefreshBindings(uiCanvas.transform);
            StartApplication();
            Canvas.ForceUpdateCanvases();
            await Task.Yield();

            if (this != null)
            {
                HideStartupCurtain();
            }
        }

        private void StartApplication()
        {
            AzureRelaySettings settings = AzureRelaySettings.Load();
            relayBaseUrl = settings.BaseUrl;
            sbiRules = sbiRuleService.LoadLocal();
            RenderSbiRuleState();
            RenderRelayState();

            if (settings.IsConfigured)
            {
                marketDataProvider = new AzureRelayMarketDataProvider(relayBaseUrl);
                openAiClient = new AzureRelayTradeAdvisorClient(relayBaseUrl);
                SetStatus("status_connected", "服务已连接，正在刷新 USD/JPY 实时行情。");
                nextQuoteRefreshAt = Time.time + QuoteRefreshSeconds;
                nextCandleRefreshAt = Time.time + CandleRefreshSeconds;
                SetWarning("warning_secret", "AI建议仅供参考，不构成投资建议。");
                _ = RefreshCandlesAndQuoteAsync(true);
                return;
            }

            SetStatus("status_relay_missing", "行情与 AI 服务暂不可用。");
            SetWarning("warning_deploy_relay", "请稍后重试；若问题持续，请联系支持。");
            nextQuoteRefreshAt = float.PositiveInfinity;
            nextCandleRefreshAt = float.PositiveInfinity;
        }

        private void Update()
        {
            UpdateLoadingAnimation();
            RefreshAdaptiveLayout();
            RefreshAdviceAgeIfNeeded();

            if (loadingOperationCount > 0 || string.IsNullOrWhiteSpace(relayBaseUrl) || !autoRefreshToggle.isOn)
            {
                return;
            }

            float now = Time.time;
            if (!quoteRefreshInFlight && !candleRefreshInFlight && now >= nextCandleRefreshAt)
            {
                nextCandleRefreshAt = now + CandleRefreshSeconds;
                nextQuoteRefreshAt = now + QuoteRefreshSeconds;
                _ = RefreshCandlesAndQuoteAsync(false);
                return;
            }

            if (!quoteRefreshInFlight && !candleRefreshInFlight && now >= nextQuoteRefreshAt)
            {
                nextQuoteRefreshAt = now + QuoteRefreshSeconds;
                _ = RefreshLatestQuoteAsync(false);
            }
        }

        private void OnDestroy()
        {
            CancelMarketDataRequests();
            CancelAdvisoryRequests();
            addressableWindowRequestVersion++;
            ReleaseActiveAddressableWindow();
            startupCurtain = null;
        }

        private void BuildUi()
        {
            EnsureEventSystem();

            Canvas canvas = CreateCanvas();
            GameObject root = CreateUiObject("Root", canvas.transform);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            Stretch(rootRect);
            Image rootBackground = root.AddComponent<Image>();
            rootBackground.color = new Color32(10, 14, 20, 255);

            GameObject topAccent = CreateUiObject("Top Accent", root.transform);
            RectTransform topAccentRect = topAccent.GetComponent<RectTransform>();
            topAccentRect.anchorMin = new Vector2(0f, 1f);
            topAccentRect.anchorMax = Vector2.one;
            topAccentRect.pivot = new Vector2(0.5f, 1f);
            topAccentRect.offsetMin = new Vector2(0f, -3f);
            topAccentRect.offsetMax = Vector2.zero;
            Image topAccentImage = topAccent.AddComponent<Image>();
            topAccentImage.color = new Color32(49, 181, 233, 210);
            topAccentImage.raycastTarget = false;

            Transform content = CreateSafeAreaContent(root.transform);
            safeAreaContentRect = content.GetComponent<RectTransform>();

            VerticalLayoutGroup contentGroup = content.gameObject.AddComponent<VerticalLayoutGroup>();
            contentGroup.padding = new RectOffset(12, 12, 12, 12);
            contentGroup.spacing = 8;
            contentGroup.childControlWidth = true;
            contentGroup.childControlHeight = true;
            contentGroup.childForceExpandWidth = true;
            contentGroup.childForceExpandHeight = false;

            BuildMarketControls(content.transform);
            BuildInputColumn(content.transform);
            BuildOutputColumn(content.transform);
            BindStaticUiTexts(root.transform);
            BindUiEvents();
            RefreshAdaptiveLayout(true);
        }

        private bool HasCompleteSceneUiReferences()
        {
            return canvasScaler != null &&
                   safeAreaContentRect != null &&
                   principalInput != null &&
                   netPositionInput != null &&
                   positionEntryPriceInput != null &&
                   intervalDropdown != null &&
                   settingsIcon != null &&
                   usageGuideIcon != null &&
                   settingsButton != null &&
                   usageGuideToolbarButton != null &&
                   settingsWindowPrefab != null &&
                   settingsWindowPrefab.RuntimeKeyIsValid() &&
                   languageWindowPrefab != null &&
                   languageWindowPrefab.RuntimeKeyIsValid() &&
                   usageGuideWindowPrefab != null &&
                   usageGuideWindowPrefab.RuntimeKeyIsValid() &&
                   adviceWindowPrefab != null &&
                   adviceWindowPrefab.RuntimeKeyIsValid() &&
                   autoRefreshToggle != null &&
                   refreshMarketButton != null &&
                   syncSbiRulesButton != null &&
                   requestAiAdviceButton != null &&
                   aggressiveAiAdviceButton != null &&
                   reopenAdviceButton != null &&
                   reopenAdviceButtonText != null &&
                   marketSourceText != null &&
                   statusText != null &&
                   sbiRulesText != null &&
                   quoteText != null &&
                   metricsText != null &&
                   warningsText != null &&
                   chartGraphic != null &&
                   loadingIndicator != null &&
                   loadingSpinnerRect != null;
        }

        private void PrepareBoundSceneUi()
        {
            EnsureEventSystem();
            ApplyRuntimeFontToCanvas();
            ConfigureAppTitleLayout();
            BindStaticUiTexts(uiCanvas.transform);
            BindUiEvents();
            UpdateAdviceButtonState();
            RefreshAdaptiveLayout(true);
        }

        private void ApplyRuntimeFontToCanvas()
        {
            if (uiCanvas == null)
            {
                return;
            }

            ApplyRuntimeFontToRoot(uiCanvas.transform);
        }

        private void ConfigureAppTitleLayout()
        {
            if (uiCanvas == null)
            {
                return;
            }

            Transform titleTransform = uiCanvas.transform.Find(AppTitlePath);
            TMP_Text title = titleTransform == null ? null : titleTransform.GetComponent<TMP_Text>();
            if (title == null)
            {
                return;
            }

            ConfigureAppTitle(title);
        }

        private static void ConfigureAppTitle(TMP_Text title)
        {
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.overflowMode = TextOverflowModes.Ellipsis;
            title.enableAutoSizing = true;
            title.fontSizeMin = 14f;
            title.fontSizeMax = 22f;
        }

        private void ApplyRuntimeFontToRoot(Transform root)
        {
            if (font == null || root == null)
            {
                return;
            }

            TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                texts[i].font = font;
            }
        }

        private void BindUiEvents()
        {
            settingsButton.onClick.RemoveListener(OnSettingsClicked);
            settingsButton.onClick.AddListener(OnSettingsClicked);

            usageGuideToolbarButton.onClick.RemoveListener(OnUsageGuideClicked);
            usageGuideToolbarButton.onClick.AddListener(OnUsageGuideClicked);

            intervalDropdown.onValueChanged.RemoveListener(OnIntervalSelected);
            intervalDropdown.onValueChanged.AddListener(OnIntervalSelected);

            refreshMarketButton.onClick.RemoveListener(OnRefreshMarketClicked);
            refreshMarketButton.onClick.AddListener(OnRefreshMarketClicked);

            syncSbiRulesButton.onClick.RemoveListener(OnSyncSbiRulesClicked);
            syncSbiRulesButton.onClick.AddListener(OnSyncSbiRulesClicked);

            requestAiAdviceButton.onClick.RemoveListener(OnConservativeAdviceClicked);
            requestAiAdviceButton.onClick.AddListener(OnConservativeAdviceClicked);

            aggressiveAiAdviceButton.onClick.RemoveListener(OnAggressiveAdviceClicked);
            aggressiveAiAdviceButton.onClick.AddListener(OnAggressiveAdviceClicked);

            reopenAdviceButton.onClick.RemoveListener(OnReopenAdviceClicked);
            reopenAdviceButton.onClick.AddListener(OnReopenAdviceClicked);
        }

        private void OnIntervalSelected(int unused)
        {
            _ = RefreshCandlesAndQuoteAsync(true);
        }

        private void OnRefreshMarketClicked()
        {
            _ = RefreshCandlesAndQuoteAsync(true);
        }

        private void OnSyncSbiRulesClicked()
        {
            _ = SyncSbiRulesAsync();
        }

        private void OnConservativeAdviceClicked()
        {
            _ = RequestAiAdviceAsync(AiTradeAdviceMode.Conservative);
        }

        private void OnAggressiveAdviceClicked()
        {
            _ = RequestAiAdviceAsync(AiTradeAdviceMode.ForcedDirectional);
        }

        private void OnSettingsClicked()
        {
            _ = ShowAddressableWindowAsync(AddressableWindowKind.Settings);
        }

        private void OnUsageGuideClicked()
        {
            _ = ShowAddressableWindowAsync(AddressableWindowKind.UsageGuide);
        }

        private void OnReopenAdviceClicked()
        {
            if (lastAdvice != null && lastAdviceGeneratedAt.HasValue)
            {
                _ = ShowAddressableWindowAsync(AddressableWindowKind.Advice);
            }
        }

        private void CloseAddressableWindow()
        {
            addressableWindowRequestVersion++;
            ReleaseActiveAddressableWindow();
        }

        private async Task ShowAddressableWindowAsync(AddressableWindowKind kind)
        {
            int requestVersion = ++addressableWindowRequestVersion;
            ReleaseActiveAddressableWindow();

            AssetReferenceGameObject reference = GetWindowReference(kind);
            string fallbackAddress = GetWindowAddress(kind);
            AsyncOperationHandle<GameObject> handle = reference != null && reference.RuntimeKeyIsValid()
                ? reference.InstantiateAsync(uiCanvas.transform, false)
                : Addressables.InstantiateAsync(fallbackAddress, uiCanvas.transform, false);

            try
            {
                await handle.Task;
            }
            catch (Exception exception)
            {
                if (requestVersion == addressableWindowRequestVersion && this != null)
                {
                    Debug.LogError($"Could not load Addressable UI window '{fallbackAddress}': {exception.Message}", this);
                }
            }

            if (requestVersion != addressableWindowRequestVersion || this == null)
            {
                ReleaseAddressableHandle(handle);
                return;
            }

            if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            {
                ReleaseAddressableHandle(handle);
                return;
            }

            activeAddressableWindow = handle.Result;
            activeAddressableWindow.name = kind + " Window";
            activeAddressableWindow.transform.SetAsLastSibling();
            activeAddressableWindowSafeAreaRect = EnsureWindowSafeAreaContent(activeAddressableWindow.transform);
            RefreshAdaptiveLayout(true);
            ApplyRuntimeFontToRoot(activeAddressableWindow.transform);
            BindStaticUiTexts(activeAddressableWindow.transform);

            if (!InitializeAddressableWindow(kind, activeAddressableWindow))
            {
                Debug.LogError($"Addressable UI window '{fallbackAddress}' has an invalid controller.", this);
                CloseAddressableWindow();
            }
        }

        private bool InitializeAddressableWindow(AddressableWindowKind kind, GameObject instance)
        {
            switch (kind)
            {
                case AddressableWindowKind.Settings:
                    FxTradeSettingsWindow settingsWindow = instance.GetComponent<FxTradeSettingsWindow>();
                    if (settingsWindow == null)
                    {
                        return false;
                    }

                    settingsWindow.Initialize(
                        () => _ = ShowAddressableWindowAsync(AddressableWindowKind.Language),
                        CloseAddressableWindow);
                    return true;

                case AddressableWindowKind.Language:
                    FxTradeLanguageWindow languageWindow = instance.GetComponent<FxTradeLanguageWindow>();
                    if (languageWindow == null)
                    {
                        return false;
                    }

                    languageWindow.Initialize(
                        RefreshLocalizedDynamicState,
                        () => _ = ShowAddressableWindowAsync(AddressableWindowKind.Settings));
                    return true;

                case AddressableWindowKind.UsageGuide:
                    FxTradeUsageGuideWindow guideWindow = instance.GetComponent<FxTradeUsageGuideWindow>();
                    if (guideWindow == null)
                    {
                        return false;
                    }

                    guideWindow.Initialize(CloseAddressableWindow);
                    return true;

                case AddressableWindowKind.Advice:
                    FxTradeAdviceWindow adviceWindow = instance.GetComponent<FxTradeAdviceWindow>();
                    if (adviceWindow == null || lastAdvice == null || !lastAdviceGeneratedAt.HasValue)
                    {
                        return false;
                    }

                    adviceWindow.Initialize(
                        latestAdviceDisplayText,
                        BuildAdviceWindowMetadata(),
                        CloseAddressableWindow);
                    return true;

                default:
                    return false;
            }
        }

        private AssetReferenceGameObject GetWindowReference(AddressableWindowKind kind)
        {
            switch (kind)
            {
                case AddressableWindowKind.Settings:
                    return settingsWindowPrefab;
                case AddressableWindowKind.Language:
                    return languageWindowPrefab;
                case AddressableWindowKind.UsageGuide:
                    return usageGuideWindowPrefab;
                case AddressableWindowKind.Advice:
                    return adviceWindowPrefab;
                default:
                    return null;
            }
        }

        private static string GetWindowAddress(AddressableWindowKind kind)
        {
            switch (kind)
            {
                case AddressableWindowKind.Settings:
                    return SettingsWindowAddress;
                case AddressableWindowKind.Language:
                    return LanguageWindowAddress;
                case AddressableWindowKind.UsageGuide:
                    return UsageGuideWindowAddress;
                case AddressableWindowKind.Advice:
                    return AdviceWindowAddress;
                default:
                    return string.Empty;
            }
        }

        private void ReleaseActiveAddressableWindow()
        {
            activeAddressableWindowSafeAreaRect = null;
            if (activeAddressableWindow == null)
            {
                return;
            }

            Addressables.ReleaseInstance(activeAddressableWindow);
            activeAddressableWindow = null;
        }

        private static void ReleaseAddressableHandle(AsyncOperationHandle<GameObject> handle)
        {
            if (!handle.IsValid())
            {
                return;
            }

            if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
            {
                Addressables.ReleaseInstance(handle.Result);
                return;
            }

            Addressables.Release(handle);
        }

        private void BuildMarketControls(Transform parent)
        {
            Transform headerCard = CreateCardSection(
                parent,
                "Header Card",
                new Color32(20, 28, 38, 255),
                70f,
                70f,
                8,
                0f);
            Transform headerBar = CreateHeaderBar(headerCard);
            Transform brandBlock = CreateBrandBlock(headerBar);
            AddHeader(brandBlock, "USD/JPY 交易助手");
            marketSourceText = AddCompactInfoText(brandBlock, "正在连接行情与 AI 服务……");

            Transform headerActions = CreateHeaderActions(headerBar);
            BuildLoadingIndicator(headerActions);
            usageGuideToolbarButton = AddToolbarButton(
                headerActions,
                "Usage Guide Button",
                "Usage Guide Icon",
                usageGuideIcon,
                "帮助");
            settingsButton = AddToolbarButton(
                headerActions,
                "Settings Button",
                "Settings Icon",
                settingsIcon,
                "设置");

            Transform marketCard = CreateCardSection(
                parent,
                "Market Overview Card",
                new Color32(18, 24, 33, 255),
                86f,
                86f,
                8,
                4f);
            Transform controls = CreateCompactFieldRow(marketCard, "Market Controls");
            AddValueRow(controls, "交易对", FxConstants.UsdJpySymbol);
            intervalDropdown = AddDropdown(controls, "周期", new List<string> { "1min", "5min", "15min" }, 1);
            autoRefreshToggle = AddToggle(controls, "实时 5秒", true);

            refreshMarketButton = AddButton(controls, "手动行情", "刷新");

            statusText = AddCompactInfoText(marketCard, string.Empty);
            statusText.color = new Color32(152, 202, 221, 255);
        }

        private void BuildInputColumn(Transform parent)
        {
            Transform advisorCard = CreateCardSection(
                parent,
                "Advisor Setup Card",
                new Color32(18, 24, 33, 255),
                166f,
                166f,
                10,
                4f);
            AddSectionTitle(advisorCard, "智能建议");
            Transform smartFields = CreateCompactFieldRow(advisorCard, "Smart Advice Fields");

            principalInput = AddInput(smartFields, "本金 JPY", "1000000", false, TMP_InputField.ContentType.Standard);
            netPositionInput = AddInput(smartFields, "净建玉数量", "0", false, TMP_InputField.ContentType.Standard);
            positionEntryPriceInput = AddInput(smartFields, "USD/JPY均价", string.Empty, false, TMP_InputField.ContentType.DecimalNumber);

            syncSbiRulesButton = AddButton(smartFields, "SBI保证金", "更新");

            Transform adviceActions = CreateCompactFieldRow(advisorCard, "AI Advice Actions");
            requestAiAdviceButton = AddButton(adviceActions, "稳健建议", "获取");

            aggressiveAiAdviceButton = AddButton(adviceActions, "积极建议", "买/卖必选");
            aggressiveAiAdviceButton.targetGraphic.color = new Color32(175, 101, 52, 255);

            sbiRulesText = AddCompactInfoText(advisorCard, "SBI保证金规则：尚未更新。");
        }

        private void BuildOutputColumn(Transform parent)
        {
            Transform insightCard = CreateCardSection(
                parent,
                "Market Insight Card",
                new Color32(16, 22, 30, 255),
                304f,
                304f,
                8,
                4f);
            quoteText = AddQuoteText(insightCard, "正在等待 USD/JPY 实时行情");

            GameObject chartPanel = CreatePanel("ChartPanel", insightCard, new Color32(9, 14, 20, 255));
            LayoutElement chartLayout = chartPanel.AddComponent<LayoutElement>();
            chartLayout.minHeight = 190f;
            chartLayout.preferredHeight = 194f;
            chartLayout.flexibleWidth = 1;
            chartLayout.flexibleHeight = 0;
            GameObject chartLine = CreateUiObject("ChartLine", chartPanel.transform);
            chartLine.AddComponent<CanvasRenderer>();
            RectTransform chartLineRect = chartLine.GetComponent<RectTransform>();
            Stretch(chartLineRect);
            chartLineRect.offsetMin = new Vector2(8f, 8f);
            chartLineRect.offsetMax = new Vector2(-8f, -8f);
            chartGraphic = chartLine.AddComponent<UsdJpyTrendLineGraphic>();
            chartGraphic.color = Color.white;
            chartGraphic.raycastTarget = false;

            metricsText = AddBodyText(insightCard, "行情指标将在此显示。");
            LayoutElement metricsLayout = metricsText.GetComponent<LayoutElement>();
            metricsLayout.minHeight = 50f;
            metricsLayout.preferredHeight = 50f;

            Transform adviceAccessCard = CreateCardSection(
                parent,
                "Advice Access Card",
                new Color32(18, 28, 38, 255),
                52f,
                52f,
                6,
                0f);
            reopenAdviceButton = AddAdviceResultButton(adviceAccessCard, out reopenAdviceButtonText);
            UpdateAdviceButtonState();

            Transform warningCard = CreateCardSection(
                parent,
                "Warning Card",
                new Color32(42, 32, 21, 255),
                48f,
                48f,
                8,
                0f);
            warningsText = AddBodyText(warningCard, string.Empty);
            LayoutElement warningsLayout = warningsText.GetComponent<LayoutElement>();
            warningsLayout.minHeight = 28;
            warningsLayout.preferredHeight = 28;
            warningsText.color = new Color32(244, 192, 102, 255);
        }

        private void RefreshLocalizedDynamicState()
        {
            RenderSbiRuleState();
            RenderRelayState();

            if (latestQuote != null)
            {
                RenderMarketMetrics(latestQuote, BuildRealtimeCandles(), GetSelectedInterval());
            }

            if (lastAdvice != null)
            {
                RenderAiAdvice(
                    lastAdvice,
                    lastAdvicePrincipalJpy,
                    lastAdviceCurrentNetLots,
                    lastAdviceAdjusted,
                    lastAdviceMode);
            }

            UpdateAdviceButtonState();
        }

        private void RefreshAdviceAgeIfNeeded()
        {
            float now = Time.unscaledTime;
            if (now < nextAdviceAgeRefreshAt)
            {
                return;
            }

            nextAdviceAgeRefreshAt = now + AdviceAgeRefreshSeconds;
            UpdateAdviceButtonState();
        }

        private void UpdateAdviceButtonState()
        {
            if (reopenAdviceButton == null || reopenAdviceButtonText == null)
            {
                return;
            }

            bool hasAdvice = lastAdvice != null &&
                             lastAdviceGeneratedAt.HasValue &&
                             !string.IsNullOrWhiteSpace(latestAdviceDisplayText);
            reopenAdviceButton.interactable = hasAdvice;
            reopenAdviceButtonText.text = hasAdvice
                ? FormatAdviceAgeLabel(lastAdviceGeneratedAt.Value, DateTime.Now)
                : FxTradeLocalization.Get("advice_button_empty", "暂无 AI 建议");
        }

        private static string FormatAdviceAgeLabel(DateTime generatedAt, DateTime now)
        {
            TimeSpan elapsed = now - generatedAt;
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            if (elapsed.TotalMinutes < 1d)
            {
                return FxTradeLocalization.Get("advice_button_just_now", "查看建议 · 刚刚");
            }

            if (elapsed.TotalHours < 1d)
            {
                return FxTradeLocalization.Get(
                    "advice_button_minutes_ago",
                    "查看建议 · {0} 分钟前",
                    Math.Max(1, (int)elapsed.TotalMinutes));
            }

            if (elapsed.TotalDays < 1d)
            {
                return FxTradeLocalization.Get(
                    "advice_button_hours_ago",
                    "查看建议 · {0} 小时前",
                    Math.Max(1, (int)elapsed.TotalHours));
            }

            return FxTradeLocalization.Get(
                "advice_button_days_ago",
                "查看建议 · {0} 天前",
                Math.Max(1, (int)elapsed.TotalDays));
        }

        private string BuildAdviceWindowMetadata()
        {
            if (!lastAdviceGeneratedAt.HasValue)
            {
                return string.Empty;
            }

            string modeLabel = lastAdviceMode == AiTradeAdviceMode.ForcedDirectional
                ? FxTradeLocalization.Get("mode_aggressive_advice", "积极建议")
                : FxTradeLocalization.Get("mode_conservative_advice", "稳健建议");
            return FxTradeLocalization.Get(
                "advice_window_generated_meta",
                "{0} · 生成于 {1:yyyy-MM-dd HH:mm}",
                modeLabel,
                lastAdviceGeneratedAt.Value);
        }

        private static void BindStaticUiTexts(Transform root)
        {
            TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text text = texts[i];
                switch (text.text)
                {
                    case "USD/JPY 交易助手":
                        FxTradeLocalization.Bind(text, "app_title", text.text);
                        break;
                    case "正在连接行情与 AI 服务……":
                        FxTradeLocalization.Bind(text, "source_loading", text.text);
                        break;
                    case "交易对":
                        FxTradeLocalization.Bind(text, "label_market_pair", text.text);
                        break;
                    case "周期":
                        FxTradeLocalization.Bind(text, "label_interval", text.text);
                        break;
                    case "实时 5秒":
                        FxTradeLocalization.Bind(text, "label_auto_refresh", text.text);
                        break;
                    case "语言":
                        FxTradeLocalization.Bind(text, "label_language", text.text);
                        break;
                    case "设置":
                        FxTradeLocalization.Bind(text, "button_settings", text.text);
                        break;
                    case "调整 App 的显示语言。":
                        FxTradeLocalization.Bind(text, "settings_description", text.text);
                        break;
                    case "语言设置":
                        FxTradeLocalization.Bind(text, "button_language_settings", text.text);
                        break;
                    case "在这里选择界面语言。":
                        FxTradeLocalization.Bind(text, "settings_language_description", text.text);
                        break;
                    case "关闭":
                        FxTradeLocalization.Bind(text, "button_close", text.text);
                        break;
                    case "使用说明":
                        FxTradeLocalization.Bind(text, "button_usage_guide", text.text);
                        break;
                    case "App 使用说明":
                        FxTradeLocalization.Bind(text, "usage_guide_title", text.text);
                        break;
                    case "AI 交易建议":
                        FxTradeLocalization.Bind(text, "advice_window_title", text.text);
                        break;
                    case "返回":
                        FxTradeLocalization.Bind(text, "button_back", text.text);
                        break;
                    case "手动行情":
                        FxTradeLocalization.Bind(text, "label_manual_market", text.text);
                        break;
                    case "刷新":
                        FxTradeLocalization.Bind(text, "button_refresh", text.text);
                        break;
                    case "智能建议":
                        FxTradeLocalization.Bind(text, "section_smart_advice", text.text);
                        break;
                    case "本金 JPY":
                        FxTradeLocalization.Bind(text, "label_principal", text.text);
                        break;
                    case "净建玉数量":
                        FxTradeLocalization.Bind(text, "label_net_position", text.text);
                        break;
                    case "USD/JPY均价":
                        FxTradeLocalization.Bind(text, "label_entry_price", text.text);
                        break;
                    case "SBI保证金":
                        FxTradeLocalization.Bind(text, "label_sbi_rules", text.text);
                        break;
                    case "更新":
                        FxTradeLocalization.Bind(text, "button_sync", text.text);
                        break;
                    case "稳健建议":
                        FxTradeLocalization.Bind(text, "label_conservative_advice", text.text);
                        break;
                    case "获取":
                        FxTradeLocalization.Bind(text, "button_get", text.text);
                        break;
                    case "积极建议":
                        FxTradeLocalization.Bind(text, "label_aggressive_advice", text.text);
                        break;
                    case "买/卖必选":
                        FxTradeLocalization.Bind(text, "button_buy_sell_required", text.text);
                        break;
                    case "SBI保证金规则：尚未更新。":
                        FxTradeLocalization.Bind(text, "sbi_not_synced_short", text.text);
                        break;
                    case "正在等待 USD/JPY 实时行情":
                        FxTradeLocalization.Bind(text, "quote_waiting", text.text);
                        break;
                    case "行情指标将在此显示。":
                        FxTradeLocalization.Bind(text, "metrics_waiting", text.text);
                        break;
                    case "输入本金和净建玉数量，更新SBI保证金规则后即可获取AI建议。":
                        FxTradeLocalization.Bind(text, "advice_initial", text.text);
                        break;
                    case "选项":
                        FxTradeLocalization.Bind(text, "dropdown_option", text.text);
                        break;
                }
            }
        }

        private async Task RefreshCandlesAndQuoteAsync(bool manual)
        {
            if (quoteRefreshInFlight || candleRefreshInFlight)
            {
                if (manual)
                {
                    SetStatus("status_market_refresh_busy", "实时行情正在刷新中。");
                }

                return;
            }

            CancellationToken token = GetMarketDataToken();
            candleRefreshInFlight = true;
            quoteRefreshInFlight = true;
            BeginLoading("loading_quote_candles", "正在获取 USD/JPY 实时报价与K线");
            nextCandleRefreshAt = Time.time + CandleRefreshSeconds;
            nextQuoteRefreshAt = Time.time + QuoteRefreshSeconds;

            try
            {
                if (string.IsNullOrWhiteSpace(relayBaseUrl))
                {
                    SetStatus("status_relay_missing", "行情与 AI 服务暂不可用。");
                    SetWarning("warning_check_config", "请检查网络连接后重试。");
                    return;
                }

                string symbol = FxConstants.UsdJpySymbol;
                SetStatus("status_fetch_quote_candles", "正在获取 {0} 实时报价与K线……", symbol);

                IFxMarketDataProvider provider = GetMarketDataProvider();
                string interval = GetSelectedInterval();

                Task<MarketQuote> quoteTask = provider.GetLatestQuoteAsync(symbol, token);
                Task<IReadOnlyList<Candle>> candlesTask = provider.GetCandlesAsync(symbol, interval, CandleOutputSize, token);
                await Task.WhenAll(quoteTask, candlesTask);

                if (token.IsCancellationRequested)
                {
                    return;
                }

                latestQuote = quoteTask.Result;
                ReplaceLatestCandles(candlesTask.Result);
                RenderLatestMarketState(
                    interval,
                    "status_provider_updated",
                    "{0} 数据已更新：{1:HH:mm:ss}",
                    provider.ProviderName,
                    DateTime.Now);
            }
            catch (OperationCanceledException)
            {
                SetStatus("status_refresh_cancelled", "已取消刷新。");
            }
            catch (Exception ex)
            {
                SetStatus("status_market_unavailable", "实时行情暂不可用。");
                SetWarning("warning_error_detail", "错误：{0}", ex.Message);
            }
            finally
            {
                candleRefreshInFlight = false;
                quoteRefreshInFlight = false;
                EndLoading();
            }
        }

        private async Task RefreshLatestQuoteAsync(bool manual)
        {
            if (quoteRefreshInFlight || candleRefreshInFlight)
            {
                if (manual)
                {
                    SetStatus("status_quote_refresh_busy", "实时报价正在刷新中。");
                }

                return;
            }

            CancellationToken token = GetMarketDataToken();
            quoteRefreshInFlight = true;
            BeginLoading("loading_quote", "正在更新 USD/JPY 实时报价");
            nextQuoteRefreshAt = Time.time + QuoteRefreshSeconds;

            try
            {
                if (string.IsNullOrWhiteSpace(relayBaseUrl))
                {
                    SetStatus("status_relay_missing", "行情与 AI 服务暂不可用。");
                    SetWarning("warning_check_config", "请检查网络连接后重试。");
                    return;
                }

                string symbol = FxConstants.UsdJpySymbol;
                SetStatus("status_update_quote", "正在更新 {0} 实时报价……", symbol);

                IFxMarketDataProvider provider = GetMarketDataProvider();
                latestQuote = await provider.GetLatestQuoteAsync(symbol, token);

                if (token.IsCancellationRequested)
                {
                    return;
                }

                RenderLatestMarketState(
                    GetSelectedInterval(),
                    "status_quote_updated",
                    "实时报价已更新：{0:HH:mm:ss}",
                    DateTime.Now);
            }
            catch (OperationCanceledException)
            {
                SetStatus("status_refresh_cancelled", "已取消刷新。");
            }
            catch (Exception ex)
            {
                SetStatus("status_market_unavailable", "实时报价暂不可用。");
                SetWarning("warning_error_detail", "错误：{0}", ex.Message);
            }
            finally
            {
                quoteRefreshInFlight = false;
                EndLoading();
            }
        }

        private void ReplaceLatestCandles(IReadOnlyList<Candle> candles)
        {
            latestCandles.Clear();

            if (candles == null)
            {
                return;
            }

            for (int i = 0; i < candles.Count; i++)
            {
                Candle candle = candles[i];
                latestCandles.Add(new Candle(candle.TimeUtc, candle.Open, candle.High, candle.Low, candle.Close));
            }
        }

        private void RenderLatestMarketState(
            string interval,
            string statusKey,
            string statusFallback,
            params object[] statusArguments)
        {
            if (latestQuote == null)
            {
                SetStatus(statusKey, statusFallback, statusArguments);
                return;
            }

            IReadOnlyList<Candle> realtimeCandles = BuildRealtimeCandles();
            chartGraphic.SetCandles(realtimeCandles);
            RenderMarketMetrics(latestQuote, realtimeCandles, interval);
            SetStatus(statusKey, statusFallback, statusArguments);
        }

        private IReadOnlyList<Candle> BuildRealtimeCandles()
        {
            List<Candle> candles = new List<Candle>(Math.Max(1, latestCandles.Count));

            for (int i = 0; i < latestCandles.Count; i++)
            {
                Candle candle = latestCandles[i];
                candles.Add(new Candle(candle.TimeUtc, candle.Open, candle.High, candle.Low, candle.Close));
            }

            if (latestQuote == null || latestQuote.Price <= 0d)
            {
                return candles;
            }

            if (candles.Count == 0)
            {
                candles.Add(new Candle(latestQuote.TimeUtc, latestQuote.Price, latestQuote.Price, latestQuote.Price, latestQuote.Price));
                return candles;
            }

            int lastIndex = candles.Count - 1;
            Candle latest = candles[lastIndex];
            double high = Math.Max(latest.High, latestQuote.Price);
            double low = Math.Min(latest.Low, latestQuote.Price);
            DateTime timeUtc = latestQuote.IsTimestampReliable && latestQuote.TimeUtc > latest.TimeUtc
                ? latestQuote.TimeUtc
                : latest.TimeUtc;

            candles[lastIndex] = new Candle(timeUtc, latest.Open, high, low, latestQuote.Price);
            return candles;
        }

        private void RenderMarketMetrics(MarketQuote quote, IReadOnlyList<Candle> candles, string interval)
        {
            string freshness = quote.IsTimestampReliable
                ? $"{quote.TimeUtc:yyyy-MM-dd HH:mm:ss} UTC"
                : FxTradeLocalization.Get("quote_timestamp_missing", "数据源时间戳不可用");

            FxTradeLocalization.Bind(
                quoteText,
                "quote_summary",
                "{0} {1:0.000}\n{2} | {3} | {4} 根K线",
                quote.Symbol,
                quote.Price,
                freshness,
                interval,
                candles.Count);

            double trendScore = TechnicalIndicatorService.CalculateTrendScore(candles, out double atrPips, out double rsi);
            double netPositionQuantity = ParseInput(netPositionInput, 0d);
            double positionEntryPrice = ParseInput(positionEntryPriceInput, 0d);
            string quantityRuleText = sbiRules != null && sbiRules.IsUsable
                ? FxTradeLocalization.Get("minimum_units", "最小 {0:N0} 通貨", sbiRules.MinimumOrderUnits)
                : FxTradeLocalization.Get("input_unit", "输入单位：通貨");
            FxTradeLocalization.Bind(
                metricsText,
                "metrics_summary",
                "趋势 {0:0.00}   RSI {1:0.0}   ATR {2:0.0} pips\n{3}   {4}",
                trendScore,
                rsi,
                atrPips,
                FormatCurrentPositionSummary(netPositionQuantity, positionEntryPrice),
                quantityRuleText);
        }

        private async Task SyncSbiRulesAsync()
        {
            if (sbiSyncInFlight)
            {
                return;
            }

            sbiSyncInFlight = true;
            syncSbiRulesButton.interactable = false;
            BeginLoading("loading_sbi", "正在更新 SBI FX 保证金规则");
            CancellationToken token = GetAdvisoryToken();

            try
            {
                SetStatus("status_reading_sbi", "正在从SBI证券官方页面获取USD/JPY保证金规则……");
                sbiRules = await sbiRuleService.RefreshAsync(token);
                RenderSbiRuleState();
                SetStatus("status_sbi_saved", "SBI FX保证金规则已保存到本机：{0:HH:mm:ss}", DateTime.Now);
                SetWarning("warning_sbi_official", "已更新USD/JPY的杠杆、必要保证金、最小交易单位和适用日期。");
            }
            catch (OperationCanceledException)
            {
                SetStatus("status_sbi_cancelled", "已取消SBI保证金规则更新。");
            }
            catch (Exception ex)
            {
                SetStatus("status_sbi_failed", "SBI FX保证金规则更新失败。");
                SetWarning("warning_error_detail", "错误：{0}", ex.Message);
            }
            finally
            {
                sbiSyncInFlight = false;
                EndLoading();
                if (syncSbiRulesButton != null)
                {
                    syncSbiRulesButton.interactable = true;
                }
            }
        }

        private async Task RequestAiAdviceAsync(AiTradeAdviceMode mode)
        {
            if (aiAdviceInFlight)
            {
                return;
            }

            if (!TryReadAdvisorInputs(out double principalJpy, out double netPositionLots, out double positionEntryPrice))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(relayBaseUrl) || openAiClient == null)
            {
                SetStatus("status_ai_unavailable", "无法请求AI建议。");
                SetWarning("warning_configure_relay", "请稍后重试；若问题持续，请联系支持。");
                return;
            }

            if (sbiRules == null || !sbiRules.IsUsable)
            {
                SetStatus("status_sync_sbi_first", "请先更新SBI FX保证金规则。");
                SetWarning("warning_sync_sbi", "点击“SBI保证金 / 更新”，取得官方规则后再获取AI建议。");
                return;
            }

            aiAdviceInFlight = true;
            SetAdviceButtonsInteractable(false);
            BeginLoading("loading_prepare_ai", "正在更新行情并准备 AI 分析");
            CancellationToken token = GetAdvisoryToken();

            try
            {
                SetStatus("status_prepare_ai", "正在更新行情并准备AI分析……");
                while (quoteRefreshInFlight || candleRefreshInFlight)
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Yield();
                }

                await RefreshCandlesAndQuoteAsync(true);
                token.ThrowIfCancellationRequested();

                if (latestQuote == null || latestCandles.Count == 0)
                {
                    throw new InvalidOperationException(FxTradeLocalization.Get(
                        "error_missing_market",
                        "缺少实时行情，暂时不能生成AI建议。"));
                }

                IReadOnlyList<Candle> realtimeCandles = BuildRealtimeCandles();
                string prompt = OpenAiTradePromptBuilder.Build(
                    principalJpy,
                    netPositionLots,
                    latestQuote,
                    realtimeCandles,
                    GetSelectedInterval(),
                    sbiRules,
                    mode,
                    positionEntryPrice,
                    FxTradeLocalization.GetAiResponseLanguageInstruction());

                bool aggressive = mode == AiTradeAdviceMode.ForcedDirectional;
                SetStatus(
                    aggressive ? "status_requesting_openai_aggressive" : "status_requesting_openai_conservative",
                    aggressive
                        ? "正在请求 OpenAI（积极策略）……"
                        : "正在请求 OpenAI（稳健策略）……");
                SetLoadingTask(
                    aggressive ? "status_wait_openai_aggressive" : "status_wait_openai_conservative",
                    aggressive
                        ? "正在等待 OpenAI 返回积极策略建议"
                        : "正在等待 OpenAI 返回稳健策略建议");
                OpenAiTradeAdvice advice = await openAiClient.GetAdviceAsync(
                    prompt,
                    mode,
                    FxTradeLocalization.GetAiResponseLanguageInstruction(),
                    token);
                bool adjusted = ApplyLocalMarginGuard(advice, principalJpy, netPositionLots, sbiRules, mode);
                lastAdviceGeneratedAt = DateTime.Now;
                RenderAiAdvice(advice, principalJpy, netPositionLots, adjusted, mode);
                _ = ShowAddressableWindowAsync(AddressableWindowKind.Advice);
                SetStatus(
                    aggressive ? "status_aggressive_updated" : "status_conservative_updated",
                    aggressive ? "积极策略已更新：{0:HH:mm:ss}" : "稳健策略已更新：{0:HH:mm:ss}",
                    DateTime.Now);
            }
            catch (OperationCanceledException)
            {
                SetStatus("status_ai_cancelled", "已取消AI建议请求。");
            }
            catch (Exception ex)
            {
                SetStatus("status_ai_failed", "AI建议请求失败。");
                SetWarning("warning_error_detail", "错误：{0}", ex.Message);
            }
            finally
            {
                aiAdviceInFlight = false;
                EndLoading();
                SetAdviceButtonsInteractable(true);
            }
        }

        private void BuildLoadingIndicator(Transform parent)
        {
            loadingIndicator = CreateUiObject("Loading Indicator", parent);
            RectTransform indicatorRect = loadingIndicator.GetComponent<RectTransform>();
            indicatorRect.sizeDelta = new Vector2(32f, 32f);
            LayoutElement indicatorLayout = loadingIndicator.AddComponent<LayoutElement>();
            indicatorLayout.minWidth = 32f;
            indicatorLayout.preferredWidth = 32f;
            indicatorLayout.minHeight = 32f;
            indicatorLayout.preferredHeight = 32f;

            Image indicatorBackground = loadingIndicator.AddComponent<Image>();
            indicatorBackground.color = new Color32(13, 19, 27, 245);
            indicatorBackground.raycastTarget = false;

            Outline indicatorOutline = loadingIndicator.AddComponent<Outline>();
            indicatorOutline.effectColor = new Color32(49, 181, 233, 125);
            indicatorOutline.effectDistance = new Vector2(1f, -1f);

            GameObject spinner = CreateUiObject("Loading Spinner", loadingIndicator.transform);
            loadingSpinnerRect = spinner.GetComponent<RectTransform>();
            loadingSpinnerRect.anchorMin = new Vector2(0.5f, 0.5f);
            loadingSpinnerRect.anchorMax = new Vector2(0.5f, 0.5f);
            loadingSpinnerRect.pivot = new Vector2(0.5f, 0.5f);
            loadingSpinnerRect.anchoredPosition = Vector2.zero;
            loadingSpinnerRect.sizeDelta = new Vector2(20f, 20f);
            CreateLoadingSpinnerDots(spinner.transform);

            loadingIndicator.SetActive(false);
        }

        private void BuildSettingsOverlay(Transform parent)
        {
            GameObject settingsOverlay = CreateUiObject("Settings Window", parent);
            RectTransform overlayRect = settingsOverlay.GetComponent<RectTransform>();
            Stretch(overlayRect);
            Image overlayBackground = settingsOverlay.AddComponent<Image>();
            overlayBackground.color = new Color32(5, 7, 10, 190);
            Transform safeAreaContent = CreateSafeAreaContent(settingsOverlay.transform);

            GameObject panel = CreatePanel(
                "Settings Panel",
                safeAreaContent,
                new Color32(31, 35, 41, 255));
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(324f, 218f);

            Outline outline = panel.AddComponent<Outline>();
            outline.effectColor = new Color32(84, 99, 116, 180);
            outline.effectDistance = new Vector2(1f, -1f);

            VerticalLayoutGroup group = panel.AddComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(18, 18, 16, 16);
            group.spacing = 10f;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = false;
            group.childForceExpandWidth = true;

            Transform titleRow = CreateSettingsTitleRow(panel.transform);
            if (settingsIcon != null)
            {
                GameObject iconObject = CreateUiObject("Settings Title Icon", titleRow);
                Image icon = iconObject.AddComponent<Image>();
                icon.sprite = settingsIcon;
                icon.color = Color.white;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                LayoutElement iconLayout = iconObject.AddComponent<LayoutElement>();
                iconLayout.minWidth = 24f;
                iconLayout.preferredWidth = 24f;
                iconLayout.minHeight = 24f;
                iconLayout.preferredHeight = 24f;
            }

            TMP_Text title = AddText(
                titleRow,
                "设置",
                18,
                FontStyles.Bold,
                new Color32(244, 247, 251, 255));
            LayoutElement titleLayout = title.gameObject.AddComponent<LayoutElement>();
            titleLayout.minHeight = 26f;
            titleLayout.preferredHeight = 26f;
            titleLayout.flexibleWidth = 1f;

            TMP_Text description = AddText(
                panel.transform,
                "调整 App 的显示语言。",
                11,
                FontStyles.Normal,
                new Color32(170, 178, 190, 255));
            LayoutElement descriptionLayout = description.gameObject.AddComponent<LayoutElement>();
            descriptionLayout.minHeight = 18f;
            descriptionLayout.preferredHeight = 18f;

            Button languageSettingsButton = AddSettingsMenuButton(panel.transform, "语言设置");
            languageSettingsButton.gameObject.name = "Language Settings Button";
            Button closeSettingsButton = AddModalButton(panel.transform, "关闭");

            FxTradeSettingsWindow window = settingsOverlay.AddComponent<FxTradeSettingsWindow>();
            window.Configure(languageSettingsButton, closeSettingsButton);
        }

        private void BuildLanguageSettingsOverlay(Transform parent)
        {
            GameObject languageOverlay = CreateUiObject("Language Window", parent);
            RectTransform overlayRect = languageOverlay.GetComponent<RectTransform>();
            Stretch(overlayRect);
            Image overlayBackground = languageOverlay.AddComponent<Image>();
            overlayBackground.color = new Color32(5, 7, 10, 205);
            Transform safeAreaContent = CreateSafeAreaContent(languageOverlay.transform);

            GameObject panel = CreatePanel(
                "Language Panel",
                safeAreaContent,
                new Color32(31, 35, 41, 255));
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(324f, 218f);

            Outline outline = panel.AddComponent<Outline>();
            outline.effectColor = new Color32(84, 99, 116, 180);
            outline.effectDistance = new Vector2(1f, -1f);

            VerticalLayoutGroup group = panel.AddComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(18, 18, 16, 16);
            group.spacing = 10f;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = false;
            group.childForceExpandWidth = true;

            TMP_Text title = AddText(
                panel.transform,
                "语言设置",
                18,
                FontStyles.Bold,
                new Color32(244, 247, 251, 255));
            LayoutElement titleLayout = title.gameObject.AddComponent<LayoutElement>();
            titleLayout.minHeight = 26f;
            titleLayout.preferredHeight = 26f;

            TMP_Text description = AddText(
                panel.transform,
                "在这里选择界面语言。",
                11,
                FontStyles.Normal,
                new Color32(170, 178, 190, 255));
            LayoutElement descriptionLayout = description.gameObject.AddComponent<LayoutElement>();
            descriptionLayout.minHeight = 18f;
            descriptionLayout.preferredHeight = 18f;

            Transform languageControls = CreateCompactFieldRow(panel.transform, "Language Settings");
            TMP_Dropdown dropdown = AddDropdown(
                languageControls,
                "语言",
                new List<string>(FxTradeLocalization.NativeLocaleNames),
                FxTradeLocalization.GetSelectedLocaleIndex());

            Button backButton = AddModalButton(panel.transform, "返回");
            backButton.gameObject.name = "Back Button";
            FxTradeLanguageWindow window = languageOverlay.AddComponent<FxTradeLanguageWindow>();
            window.Configure(dropdown, backButton);
        }

        private void BuildUsageGuideOverlay(Transform parent)
        {
            GameObject usageGuideOverlay = CreateUiObject("Usage Guide Window", parent);
            RectTransform overlayRect = usageGuideOverlay.GetComponent<RectTransform>();
            Stretch(overlayRect);
            Image overlayBackground = usageGuideOverlay.AddComponent<Image>();
            overlayBackground.color = new Color32(7, 11, 16, 255);
            Transform safeAreaContent = CreateSafeAreaContent(usageGuideOverlay.transform);

            GameObject page = CreatePanel(
                "Usage Guide Page",
                safeAreaContent,
                new Color32(18, 25, 35, 255));
            RectTransform pageRect = page.GetComponent<RectTransform>();
            pageRect.anchorMin = new Vector2(0.04f, 0.035f);
            pageRect.anchorMax = new Vector2(0.96f, 0.965f);
            pageRect.offsetMin = Vector2.zero;
            pageRect.offsetMax = Vector2.zero;

            Outline pageOutline = page.AddComponent<Outline>();
            pageOutline.effectColor = new Color32(49, 181, 233, 150);
            pageOutline.effectDistance = new Vector2(1f, -1f);

            VerticalLayoutGroup pageLayout = page.AddComponent<VerticalLayoutGroup>();
            pageLayout.padding = new RectOffset(16, 16, 16, 16);
            pageLayout.spacing = 12f;
            pageLayout.childControlHeight = true;
            pageLayout.childControlWidth = true;
            pageLayout.childForceExpandHeight = false;
            pageLayout.childForceExpandWidth = true;

            Transform header = CreateUsageGuideHeader(page.transform);
            Button closeUsageGuideButton = AddUsageGuideBackButton(header, "返回");
            TMP_Text title = AddText(
                header,
                "App 使用说明",
                19,
                FontStyles.Bold,
                new Color32(244, 247, 251, 255));
            title.alignment = TextAlignmentOptions.MidlineLeft;
            LayoutElement titleLayout = title.gameObject.AddComponent<LayoutElement>();
            titleLayout.minHeight = 38f;
            titleLayout.preferredHeight = 38f;
            titleLayout.flexibleWidth = 1f;

            Transform scrollContent = CreateUsageGuideScrollView(page.transform);
            TMP_Text usageGuideBodyText = AddText(
                scrollContent,
                UsageGuideFallback,
                13,
                FontStyles.Normal,
                new Color32(213, 221, 232, 255));
            usageGuideBodyText.gameObject.name = "Usage Guide Body";
            usageGuideBodyText.alignment = TextAlignmentOptions.TopLeft;
            usageGuideBodyText.textWrappingMode = TextWrappingModes.Normal;
            usageGuideBodyText.overflowMode = TextOverflowModes.Overflow;
            usageGuideBodyText.lineSpacing = 7f;
            LayoutElement bodyLayout = usageGuideBodyText.gameObject.AddComponent<LayoutElement>();
            bodyLayout.minWidth = 0f;
            bodyLayout.flexibleWidth = 1f;
            ContentSizeFitter bodyFitter = usageGuideBodyText.gameObject.AddComponent<ContentSizeFitter>();
            bodyFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            FxTradeLocalization.Bind(
                usageGuideBodyText,
                "usage_guide_body",
                UsageGuideFallback);

            FxTradeUsageGuideWindow window = usageGuideOverlay.AddComponent<FxTradeUsageGuideWindow>();
            window.Configure(closeUsageGuideButton, usageGuideBodyText);
        }

        private void BuildAdviceOverlay(Transform parent)
        {
            GameObject adviceOverlay = CreateUiObject("Advice Window", parent);
            RectTransform overlayRect = adviceOverlay.GetComponent<RectTransform>();
            Stretch(overlayRect);
            Image overlayBackground = adviceOverlay.AddComponent<Image>();
            overlayBackground.color = new Color32(5, 9, 14, 238);
            Transform safeAreaContent = CreateSafeAreaContent(adviceOverlay.transform);

            GameObject page = CreatePanel(
                "Advice Page",
                safeAreaContent,
                new Color32(15, 23, 31, 255));
            RectTransform pageRect = page.GetComponent<RectTransform>();
            pageRect.anchorMin = new Vector2(0.035f, 0.03f);
            pageRect.anchorMax = new Vector2(0.965f, 0.97f);
            pageRect.offsetMin = Vector2.zero;
            pageRect.offsetMax = Vector2.zero;

            Outline pageOutline = page.AddComponent<Outline>();
            pageOutline.effectColor = new Color32(123, 217, 171, 175);
            pageOutline.effectDistance = new Vector2(1f, -1f);

            VerticalLayoutGroup pageLayout = page.AddComponent<VerticalLayoutGroup>();
            pageLayout.padding = new RectOffset(16, 16, 16, 16);
            pageLayout.spacing = 10f;
            pageLayout.childControlHeight = true;
            pageLayout.childControlWidth = true;
            pageLayout.childForceExpandHeight = false;
            pageLayout.childForceExpandWidth = true;

            Transform header = CreateUsageGuideHeader(page.transform);
            header.gameObject.name = "Advice Header";
            TMP_Text title = AddText(
                header,
                "AI 交易建议",
                20,
                FontStyles.Bold,
                new Color32(244, 247, 251, 255));
            title.alignment = TextAlignmentOptions.MidlineLeft;
            LayoutElement titleLayout = title.gameObject.AddComponent<LayoutElement>();
            titleLayout.minHeight = 38f;
            titleLayout.preferredHeight = 38f;
            titleLayout.flexibleWidth = 1f;

            Button closeButton = AddUsageGuideBackButton(header, "关闭");
            closeButton.gameObject.name = "Close Button";

            TMP_Text generatedAtText = AddText(
                page.transform,
                "稳健建议 · 生成于 2026-07-16 18:42",
                11,
                FontStyles.Normal,
                new Color32(123, 217, 171, 255));
            generatedAtText.gameObject.name = "Generated At Text";
            generatedAtText.alignment = TextAlignmentOptions.MidlineLeft;
            LayoutElement generatedAtLayout = generatedAtText.gameObject.AddComponent<LayoutElement>();
            generatedAtLayout.minHeight = 22f;
            generatedAtLayout.preferredHeight = 22f;

            Transform scrollContent = CreateUsageGuideScrollView(page.transform);
            scrollContent.parent.parent.gameObject.name = "Advice Scroll View";
            TMP_Text adviceBodyText = AddText(
                scrollContent,
                "稳健建议：观望   置信度 78%\n\n当前价格接近短期阻力位，动量尚未形成明确突破。建议等待价格确认方向后再考虑建仓。\n\n依据：短周期均线趋于平缓，波动率回落，当前风险收益比不足。\n\n风险提示\nAI 建议仅供参考，请结合本金、现有持仓与 SBI 保证金规则自行判断。",
                14,
                FontStyles.Normal,
                new Color32(224, 231, 239, 255));
            adviceBodyText.gameObject.name = "Advice Body";
            adviceBodyText.alignment = TextAlignmentOptions.TopLeft;
            adviceBodyText.textWrappingMode = TextWrappingModes.Normal;
            adviceBodyText.overflowMode = TextOverflowModes.Overflow;
            adviceBodyText.lineSpacing = 9f;
            LayoutElement bodyLayout = adviceBodyText.gameObject.AddComponent<LayoutElement>();
            bodyLayout.minWidth = 0f;
            bodyLayout.flexibleWidth = 1f;
            ContentSizeFitter bodyFitter = adviceBodyText.gameObject.AddComponent<ContentSizeFitter>();
            bodyFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            FxTradeAdviceWindow window = adviceOverlay.AddComponent<FxTradeAdviceWindow>();
            window.Configure(closeButton, generatedAtText, adviceBodyText);
        }

        private void CreateLoadingSpinnerDots(Transform parent)
        {
            const int dotCount = 8;
            const float radius = 8f;
            for (int i = 0; i < dotCount; i++)
            {
                float angle = (Mathf.PI * 2f * i) / dotCount;
                GameObject dot = CreateUiObject($"Dot {i + 1}", parent);
                RectTransform dotRect = dot.GetComponent<RectTransform>();
                dotRect.anchorMin = new Vector2(0.5f, 0.5f);
                dotRect.anchorMax = new Vector2(0.5f, 0.5f);
                dotRect.pivot = new Vector2(0.5f, 0.5f);
                dotRect.anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * radius;
                dotRect.sizeDelta = new Vector2(3f, 5f);
                dotRect.localRotation = Quaternion.Euler(0f, 0f, -angle * Mathf.Rad2Deg);

                Image dotImage = dot.AddComponent<Image>();
                float alpha = Mathf.Lerp(0.28f, 1f, (i + 1f) / dotCount);
                dotImage.color = new Color(0.48f, 0.85f, 0.67f, alpha);
                dotImage.raycastTarget = false;
            }
        }

        private void BeginLoading(string key, string fallback, params object[] arguments)
        {
            loadingOperationCount++;
            SetLoadingTask(key, fallback, arguments);

            if (loadingOperationCount != 1)
            {
                return;
            }

            if (loadingIndicator != null)
            {
                loadingIndicator.SetActive(true);
                LayoutRebuilder.ForceRebuildLayoutImmediate(
                    loadingIndicator.transform.parent as RectTransform);
            }
        }

        private void SetLoadingTask(string key, string fallback, params object[] arguments)
        {
            SetStatus(
                string.IsNullOrWhiteSpace(key) ? "status_processing" : key,
                string.IsNullOrWhiteSpace(fallback) ? "正在处理请求" : fallback,
                arguments);
        }

        private void EndLoading()
        {
            loadingOperationCount = Mathf.Max(0, loadingOperationCount - 1);
            if (loadingOperationCount > 0)
            {
                return;
            }

            if (loadingIndicator != null)
            {
                loadingIndicator.SetActive(false);
                LayoutRebuilder.ForceRebuildLayoutImmediate(
                    loadingIndicator.transform.parent as RectTransform);
            }
        }

        private void UpdateLoadingAnimation()
        {
            if (loadingOperationCount > 0 && loadingSpinnerRect != null)
            {
                loadingSpinnerRect.Rotate(0f, 0f, -180f * Time.unscaledDeltaTime);
            }
        }

        private void SetAdviceButtonsInteractable(bool interactable)
        {
            if (requestAiAdviceButton != null)
            {
                requestAiAdviceButton.interactable = interactable;
            }

            if (aggressiveAiAdviceButton != null)
            {
                aggressiveAiAdviceButton.interactable = interactable;
            }
        }

        private bool TryReadAdvisorInputs(out double principalJpy, out double netPositionLots, out double positionEntryPrice)
        {
            principalJpy = 0d;
            netPositionLots = 0d;
            positionEntryPrice = 0d;

            if (principalInput == null ||
                !double.TryParse(principalInput.text, UserNumberStyles, CultureInfo.InvariantCulture, out principalJpy) ||
                double.IsNaN(principalJpy) ||
                double.IsInfinity(principalJpy) ||
                principalJpy <= 0d)
            {
                SetStatus("status_invalid_input", "输入内容有误。");
                SetWarning("warning_principal", "本金必须是大于0的JPY金额。");
                return false;
            }

            if (netPositionInput == null ||
                !double.TryParse(netPositionInput.text, UserNumberStyles, CultureInfo.InvariantCulture, out double netPositionQuantity) ||
                double.IsNaN(netPositionQuantity) ||
                double.IsInfinity(netPositionQuantity))
            {
                SetStatus("status_invalid_input", "输入内容有误。");
                SetWarning("warning_position", "净建玉数量必须是数字：正数为多头，负数为空头，0为空仓。单位为通貨。");
                return false;
            }

            string entryPriceText = positionEntryPriceInput == null ? string.Empty : positionEntryPriceInput.text;
            if (!string.IsNullOrWhiteSpace(entryPriceText) &&
                (!double.TryParse(entryPriceText, UserNumberStyles, CultureInfo.InvariantCulture, out positionEntryPrice) ||
                 double.IsNaN(positionEntryPrice) ||
                 double.IsInfinity(positionEntryPrice) ||
                 positionEntryPrice <= 0d))
            {
                SetStatus("status_invalid_input", "输入内容有误。");
                SetWarning("warning_entry_price", "USD/JPY建仓价必须是大于0的价格。");
                return false;
            }

            if (Math.Abs(netPositionQuantity) > 0.0000001d && positionEntryPrice <= 0d)
            {
                SetStatus("status_invalid_input", "输入内容有误。");
                SetWarning("warning_missing_entry", "当前净建玉数量不为0时，请填写USD/JPY建仓价：数量正数=买入，负数=卖出。");
                return false;
            }

            netPositionLots = CurrencyUnitsToStandardLots(netPositionQuantity);
            return true;
        }

        private static bool ApplyLocalMarginGuard(
            OpenAiTradeAdvice advice,
            double principalJpy,
            double netPositionLots,
            SbiFxRuleSnapshot rules,
            AiTradeAdviceMode mode)
        {
            if (advice == null || rules == null || rules.RequiredMarginPerStandardLotJpy <= 0d)
            {
                return false;
            }

            string originalAction = advice.action;
            double originalLots = advice.suggested_lots;
            double marginLimitRatio = OpenAiTradePromptBuilder.GetMarginLimitRatio(mode);
            double maxGrossLots = (principalJpy * marginLimitRatio) / rules.RequiredMarginPerStandardLotJpy;
            double maximumBuyOrderLots = Math.Max(0d, maxGrossLots - netPositionLots);
            double maximumSellOrderLots = Math.Max(0d, maxGrossLots + netPositionLots);
            double minimumLotStep = Math.Max(0.001d, rules.MinimumOrderUnits / FxConstants.StandardLotBaseUnits);
            double maxOrderLots = GetMaximumOrderLots(advice.action, maximumBuyOrderLots, maximumSellOrderLots);
            bool directionChanged = false;

            if (mode == AiTradeAdviceMode.ForcedDirectional && maxOrderLots < minimumLotStep)
            {
                string alternateAction = string.Equals(advice.action, "BUY", StringComparison.Ordinal) ? "SELL" : "BUY";
                double alternateMaximum = GetMaximumOrderLots(alternateAction, maximumBuyOrderLots, maximumSellOrderLots);
                if (alternateMaximum >= minimumLotStep)
                {
                    advice.action = alternateAction;
                    maxOrderLots = alternateMaximum;
                    directionChanged = true;
                    advice.summary = FxTradeLocalization.Get(
                        "guard_summary_reverse",
                        "原方向超出保证金限制，改为反向小额交易");
                    advice.reasoning = FxTradeLocalization.Get(
                        "guard_reason_reverse",
                        "模型原方向在本地保证金上限下不可执行，因此选择可执行的反方向最小单。");
                }
            }

            if (!string.Equals(advice.action, "BUY", StringComparison.Ordinal) &&
                !string.Equals(advice.action, "SELL", StringComparison.Ordinal))
            {
                advice.action = "HOLD";
                advice.suggested_lots = 0d;
                return originalLots != 0d;
            }

            double requestedLots = directionChanged ? minimumLotStep : Math.Max(0d, advice.suggested_lots);
            if (mode == AiTradeAdviceMode.ForcedDirectional && requestedLots < minimumLotStep && maxOrderLots >= minimumLotStep)
            {
                requestedLots = minimumLotStep;
            }

            double guardedLots = Math.Min(requestedLots, maxOrderLots);
            guardedLots = Math.Floor((guardedLots + 0.0000001d) / minimumLotStep) * minimumLotStep;

            if (guardedLots < minimumLotStep)
            {
                guardedLots = 0d;
                advice.action = "HOLD";
            }

            advice.suggested_lots = guardedLots;
            bool adjusted = directionChanged ||
                !string.Equals(originalAction, advice.action, StringComparison.Ordinal) ||
                Math.Abs(originalLots - guardedLots) > 0.0000001d;
            if (adjusted)
            {
                advice.risk_warning = string.IsNullOrWhiteSpace(advice.risk_warning)
                    ? FxTradeLocalization.Get(
                        "guard_adjusted_empty",
                        "建议方向或建玉数量已由本地保证金保护规则调整。")
                    : FxTradeLocalization.Get(
                        "guard_adjusted_append",
                        "{0} 本地保证金保护规则已调整建议方向或建玉数量。",
                        advice.risk_warning);
            }

            return adjusted;
        }

        private static double GetMaximumOrderLots(string action, double maximumBuyOrderLots, double maximumSellOrderLots)
        {
            if (string.Equals(action, "BUY", StringComparison.Ordinal))
            {
                return maximumBuyOrderLots;
            }

            if (string.Equals(action, "SELL", StringComparison.Ordinal))
            {
                return maximumSellOrderLots;
            }

            return 0d;
        }

        private void RenderAiAdvice(
            OpenAiTradeAdvice advice,
            double principalJpy,
            double currentNetLots,
            bool adjusted,
            AiTradeAdviceMode mode)
        {
            lastAdvice = advice;
            lastAdvicePrincipalJpy = principalJpy;
            lastAdviceCurrentNetLots = currentNetLots;
            lastAdviceAdjusted = adjusted;
            lastAdviceMode = mode;

            string actionLabel;
            double postTradeNetLots = currentNetLots;
            switch (advice.action)
            {
                case "BUY":
                    actionLabel = FxTradeLocalization.Get("action_buy", "买入");
                    postTradeNetLots += advice.suggested_lots;
                    break;
                case "SELL":
                    actionLabel = FxTradeLocalization.Get("action_sell", "卖出");
                    postTradeNetLots -= advice.suggested_lots;
                    break;
                default:
                    actionLabel = FxTradeLocalization.Get("action_hold", "观望");
                    break;
            }

            double postTradeMargin = Math.Abs(postTradeNetLots) * sbiRules.RequiredMarginPerStandardLotJpy;
            double marginUsagePercent = principalJpy > 0d ? (postTradeMargin / principalJpy) * 100d : 0d;
            string modeLabel = mode == AiTradeAdviceMode.ForcedDirectional
                ? FxTradeLocalization.Get("mode_aggressive_advice", "积极建议")
                : FxTradeLocalization.Get("mode_conservative_advice", "稳健建议");
            string adviceResult = FxTradeLocalization.Get(
                "advice_result",
                "{0}：{1} 建玉数量 {2}   置信度 {3:P0}\n{4}\n依据：{5}",
                modeLabel,
                actionLabel,
                FormatQuantityFromLots(advice.suggested_lots),
                advice.confidence,
                NormalizeVisibleAdviceText(advice.summary),
                NormalizeVisibleAdviceText(advice.reasoning));

            string adjustedText = adjusted
                ? FxTradeLocalization.Get("guard_applied", " 已执行本地建玉数量保护。")
                : string.Empty;
            string forcedDirectionWarning = mode == AiTradeAdviceMode.ForcedDirectional
                ? FxTradeLocalization.Get("forced_direction_warning", " 积极模式会强制选择方向，不代表信号充分。")
                : string.Empty;

            string riskSummary = FxTradeLocalization.Get(
                "warning_ai_result",
                "{0} 预计交易后净建玉数量 {1}，保证金占本金 {2:0.0}%。{3}{4} AI建议仅供参考，不构成投资建议。",
                NormalizeVisibleAdviceText(advice.risk_warning),
                FormatQuantityFromLots(postTradeNetLots),
                marginUsagePercent,
                adjustedText,
                forcedDirectionWarning);
            latestAdviceDisplayText = FxTradeLocalization.Get(
                "advice_window_body",
                "{0}\n\n风险提示\n{1}",
                adviceResult,
                riskSummary);
            UpdateAdviceButtonState();

            SetWarning(
                "warning_ai_result",
                "{0} 预计交易后净建玉数量 {1}，保证金占本金 {2:0.0}%。{3}{4} AI建议仅供参考，不构成投资建议。",
                NormalizeVisibleAdviceText(advice.risk_warning),
                FormatQuantityFromLots(postTradeNetLots),
                marginUsagePercent,
                adjustedText,
                forcedDirectionWarning);
        }

        private void RenderSbiRuleState()
        {
            if (sbiRulesText == null)
            {
                return;
            }

            if (sbiRules == null || !sbiRules.IsUsable)
            {
                FxTradeLocalization.Bind(
                    sbiRulesText,
                    "sbi_not_synced",
                    "SBI保证金规则：尚未更新，请先取得官方最新规则。");
                return;
            }

            FxTradeLocalization.Bind(
                sbiRulesText,
                "sbi_summary",
                "SBI保证金规则：{0}倍 / {1:0.##}% / 每1万通貨 {2:N0} JPY / 适用 {3} / 最小 {4:N0} 通貨",
                sbiRules.Leverage,
                sbiRules.MarginRatePercent,
                sbiRules.RequiredMarginPer10000Jpy,
                sbiRules.ApplicableDate,
                sbiRules.MinimumOrderUnits);
        }

        private CancellationToken GetMarketDataToken()
        {
            if (marketDataCancellation == null || marketDataCancellation.IsCancellationRequested)
            {
                marketDataCancellation = new CancellationTokenSource();
            }

            return marketDataCancellation.Token;
        }

        private CancellationToken GetAdvisoryToken()
        {
            if (advisoryCancellation == null || advisoryCancellation.IsCancellationRequested)
            {
                advisoryCancellation = new CancellationTokenSource();
            }

            return advisoryCancellation.Token;
        }

        private IFxMarketDataProvider GetMarketDataProvider()
        {
            if (marketDataProvider == null)
            {
                marketDataProvider = new AzureRelayMarketDataProvider(relayBaseUrl);
            }

            return marketDataProvider;
        }

        private string GetSelectedInterval()
        {
            if (intervalDropdown == null || intervalDropdown.options == null || intervalDropdown.options.Count == 0)
            {
                return "5min";
            }

            int index = Mathf.Clamp(intervalDropdown.value, 0, intervalDropdown.options.Count - 1);
            return intervalDropdown.options[index].text;
        }

        private void RenderRelayState()
        {
            if (marketSourceText == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(relayBaseUrl))
            {
                FxTradeLocalization.Bind(
                    marketSourceText,
                    "relay_not_configured",
                    "行情与AI服务：暂不可用");
                return;
            }

            FxTradeLocalization.Bind(
                marketSourceText,
                "relay_configured",
                "行情与AI服务：已连接");
        }

        private void CancelMarketDataRequests()
        {
            if (marketDataCancellation == null)
            {
                return;
            }

            marketDataCancellation.Cancel();
            marketDataCancellation.Dispose();
            marketDataCancellation = null;
        }

        private void CancelAdvisoryRequests()
        {
            if (advisoryCancellation == null)
            {
                return;
            }

            advisoryCancellation.Cancel();
            advisoryCancellation.Dispose();
            advisoryCancellation = null;
        }

        private static double CurrencyUnitsToStandardLots(double quantity)
        {
            return quantity / FxConstants.StandardLotBaseUnits;
        }

        private static double StandardLotsToCurrencyUnits(double lots)
        {
            return lots * FxConstants.StandardLotBaseUnits;
        }

        private static string FormatQuantityFromLots(double lots)
        {
            return FxTradeLocalization.Get(
                "formatted_quantity",
                "{0} 通貨",
                FormatBaseCurrencyUnits(StandardLotsToCurrencyUnits(lots)));
        }

        private static string FormatBaseCurrencyUnits(double quantity)
        {
            double rounded = Math.Round(quantity, 0, MidpointRounding.AwayFromZero);
            return rounded.ToString("N0", CultureInfo.InvariantCulture);
        }

        private static string FormatCurrentPositionSummary(double netPositionQuantity, double entryPrice)
        {
            if (Math.Abs(netPositionQuantity) <= 0.0000001d)
            {
                return FxTradeLocalization.Get("position_flat", "当前净建玉数量 0 通貨（空仓）");
            }

            string sideLabel = netPositionQuantity > 0d
                ? FxTradeLocalization.Get("position_side_long", "正数=买入")
                : FxTradeLocalization.Get("position_side_short", "负数=卖出");
            string entryText = entryPrice > 0d
                ? FxTradeLocalization.Get(
                    "position_entry_present",
                    "，建仓价 USD/JPY {0}",
                    FormatUsdJpyPrice(entryPrice))
                : FxTradeLocalization.Get("position_entry_missing", "，建仓价未填写");
            return FxTradeLocalization.Get(
                "position_summary",
                "当前净建玉数量 {0} 通貨（{1}{2}）",
                FormatBaseCurrencyUnits(netPositionQuantity),
                sideLabel,
                entryText);
        }

        private static string FormatUsdJpyPrice(double price)
        {
            return price.ToString("0.000", CultureInfo.InvariantCulture);
        }

        private static string NormalizeVisibleAdviceText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string normalized = VisibleLotQuantityPattern.Replace(value, match =>
            {
                string rawValue = match.Groups["value"].Value;
                if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double lots))
                {
                    return match.Value;
                }

                return FxTradeLocalization.Get(
                    "normalized_position_size",
                    "建玉数量 {0}",
                    FormatQuantityFromLots(lots));
            });

            string localizedPositionSize = FxTradeLocalization.Get("position_size", "建玉数量");
            return VisibleLotWordPattern.Replace(normalized, localizedPositionSize)
                .Replace("手数", localizedPositionSize)
                .Replace("ロット", localizedPositionSize);
        }

        private double ParseInput(TMP_InputField input, double fallback)
        {
            if (input == null || !double.TryParse(input.text, UserNumberStyles, CultureInfo.InvariantCulture, out double value))
            {
                return fallback;
            }

            return value;
        }

        private void SetStatus(string key, string fallback, params object[] arguments)
        {
            FxTradeLocalization.Bind(statusText, key, fallback, arguments);
        }

        private void SetWarning(string key, string fallback, params object[] arguments)
        {
            if (arguments == null || arguments.Length == 0)
            {
                arguments = Array.Empty<object>();
            }

            FxTradeLocalization.Bind(
                warningsText,
                string.IsNullOrWhiteSpace(key) ? "warning_default" : key,
                string.IsNullOrWhiteSpace(fallback) ? "AI建议仅供参考，不构成投资建议。" : fallback,
                arguments);
        }

        private static void ConfigurePortraitRuntime()
        {
            Screen.autorotateToPortrait = true;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
            Screen.orientation = ScreenOrientation.Portrait;
        }

        private void RefreshAdaptiveLayout(bool force = false)
        {
            if (canvasScaler == null || safeAreaContentRect == null || Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }

            Rect safeArea = Screen.safeArea;
            Vector2Int screenSize = new Vector2Int(Screen.width, Screen.height);
            if (safeArea.width <= 0f || safeArea.height <= 0f)
            {
                safeArea = new Rect(0f, 0f, screenSize.x, screenSize.y);
            }

            if (!force && safeArea == lastSafeArea && screenSize == lastScreenSize)
            {
                return;
            }

            lastSafeArea = safeArea;
            lastScreenSize = screenSize;
            ApplySafeArea(safeAreaContentRect, safeArea, screenSize);
            if (activeAddressableWindowSafeAreaRect != null)
            {
                ApplySafeArea(activeAddressableWindowSafeAreaRect, safeArea, screenSize);
            }

            float safeWidthRatio = Mathf.Clamp(safeArea.width / screenSize.x, 0.01f, 1f);
            float safeHeightRatio = Mathf.Clamp(safeArea.height / screenSize.y, 0.01f, 1f);
            canvasScaler.referenceResolution = new Vector2(
                MobileReferenceResolution.x / safeWidthRatio,
                MobileReferenceResolution.y / safeHeightRatio);

            LayoutRebuilder.ForceRebuildLayoutImmediate(safeAreaContentRect);
            if (activeAddressableWindowSafeAreaRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(activeAddressableWindowSafeAreaRect);
            }
        }

        private static void ApplySafeArea(RectTransform rect, Rect safeArea, Vector2Int screenSize)
        {
            Vector2 anchorMin = safeArea.position;
            Vector2 anchorMax = safeArea.position + safeArea.size;
            anchorMin.x /= screenSize.x;
            anchorMin.y /= screenSize.y;
            anchorMax.x /= screenSize.x;
            anchorMax.y /= screenSize.y;

            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Transform CreateSafeAreaContent(Transform parent)
        {
            GameObject content = new GameObject("Safe Area Content", typeof(RectTransform));
            content.transform.SetParent(parent, false);
            Stretch(content.GetComponent<RectTransform>());
            return content.transform;
        }

        private static RectTransform EnsureWindowSafeAreaContent(Transform windowRoot)
        {
            RectTransform existing = windowRoot.Find("Safe Area Content") as RectTransform;
            if (existing != null)
            {
                return existing;
            }

            List<Transform> windowChildren = new List<Transform>(windowRoot.childCount);
            for (int i = 0; i < windowRoot.childCount; i++)
            {
                windowChildren.Add(windowRoot.GetChild(i));
            }

            Transform safeAreaContent = CreateSafeAreaContent(windowRoot);
            safeAreaContent.SetAsFirstSibling();
            for (int i = 0; i < windowChildren.Count; i++)
            {
                windowChildren[i].SetParent(safeAreaContent, false);
            }

            return safeAreaContent.GetComponent<RectTransform>();
        }

        private Canvas CreateCanvas()
        {
            Canvas canvas = GetComponent<Canvas>();
            GameObject canvasObject;
            if (canvas == null)
            {
                canvasObject = new GameObject("USDJPY Advisor Canvas");
                canvas = canvasObject.AddComponent<Canvas>();
            }
            else
            {
                canvasObject = gameObject;
            }

            uiCanvas = canvas;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            if (canvasObject.GetComponent<GraphicRaycaster>() == null)
            {
                canvasObject.AddComponent<GraphicRaycaster>();
            }

            canvasScaler = canvasObject.GetComponent<CanvasScaler>();
            if (canvasScaler == null)
            {
                canvasScaler = canvasObject.AddComponent<CanvasScaler>();
            }

            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            canvasScaler.referenceResolution = MobileReferenceResolution;
            return canvas;
        }

        private GameObject CreatePanel(string name, Transform parent, Color32 color)
        {
            GameObject panel = CreateUiObject(name, parent);
            Image image = panel.AddComponent<Image>();
            image.color = color;
            return panel;
        }

        private Transform CreateCardSection(
            Transform parent,
            string name,
            Color32 color,
            float minHeight,
            float preferredHeight,
            int padding,
            float spacing,
            float flexibleHeight = 0f)
        {
            GameObject card = CreatePanel(name, parent, color);
            LayoutElement cardLayout = card.AddComponent<LayoutElement>();
            cardLayout.minHeight = minHeight;
            cardLayout.preferredHeight = preferredHeight;
            cardLayout.flexibleHeight = flexibleHeight;
            cardLayout.flexibleWidth = 1f;

            Outline outline = card.AddComponent<Outline>();
            outline.effectColor = new Color32(64, 82, 103, 125);
            outline.effectDistance = new Vector2(1f, -1f);

            VerticalLayoutGroup group = card.AddComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(padding, padding, padding, padding);
            group.spacing = spacing;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = false;
            group.childForceExpandWidth = true;
            return card.transform;
        }

        private GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject uiObject = new GameObject(name, typeof(RectTransform));
            uiObject.transform.SetParent(parent, false);
            RectTransform rect = uiObject.GetComponent<RectTransform>();
            rect.localScale = Vector3.one;
            return uiObject;
        }

        private TMP_Text AddHeader(Transform parent, string text)
        {
            TMP_Text label = AddText(parent, text, 22, FontStyles.Bold, new Color32(244, 247, 251, 255));
            ConfigureAppTitle(label);
            LayoutElement layout = label.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 32;
            layout.preferredHeight = 32;
            layout.flexibleWidth = 1;
            return label;
        }

        private void ShowStartupCurtain()
        {
            if (uiCanvas == null)
            {
                return;
            }

            Transform existing = uiCanvas.transform.Find(StartupCurtainName);
            if (existing != null)
            {
                startupCurtain = existing.gameObject;
                startupCurtain.SetActive(true);
                startupCurtain.transform.SetAsLastSibling();
                return;
            }

            startupCurtain = CreateUiObject(StartupCurtainName, uiCanvas.transform);
            Stretch(startupCurtain.GetComponent<RectTransform>());
            Image curtainImage = startupCurtain.AddComponent<Image>();
            curtainImage.color = Color.black;
            curtainImage.raycastTarget = true;
            startupCurtain.transform.SetAsLastSibling();
        }

        private void HideStartupCurtain()
        {
            if (startupCurtain == null)
            {
                return;
            }

            DestroyRuntimeObject(startupCurtain);
            startupCurtain = null;
        }

        private Transform CreateHeaderBar(Transform parent)
        {
            GameObject row = CreateUiObject("Header Bar", parent);
            LayoutElement rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.minHeight = 54f;
            rowLayout.preferredHeight = 54f;

            HorizontalLayoutGroup group = row.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 8f;
            group.childAlignment = TextAnchor.MiddleCenter;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = true;
            group.childForceExpandWidth = false;
            return row.transform;
        }

        private Transform CreateBrandBlock(Transform parent)
        {
            GameObject brand = CreateUiObject("Brand Block", parent);
            LayoutElement brandLayout = brand.AddComponent<LayoutElement>();
            brandLayout.minWidth = 0f;
            brandLayout.flexibleWidth = 1f;

            VerticalLayoutGroup group = brand.AddComponent<VerticalLayoutGroup>();
            group.spacing = 0f;
            group.childAlignment = TextAnchor.MiddleLeft;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = false;
            group.childForceExpandWidth = true;
            return brand.transform;
        }

        private Transform CreateHeaderActions(Transform parent)
        {
            GameObject actions = CreateUiObject("Header Actions", parent);
            LayoutElement actionsLayout = actions.AddComponent<LayoutElement>();
            actionsLayout.minWidth = 116f;
            actionsLayout.preferredWidth = 116f;
            actionsLayout.minHeight = 36f;
            actionsLayout.preferredHeight = 36f;

            HorizontalLayoutGroup group = actions.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 6f;
            group.childAlignment = TextAnchor.MiddleRight;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = false;
            group.childForceExpandWidth = false;
            return actions.transform;
        }

        private Transform CreateSettingsTitleRow(Transform parent)
        {
            GameObject row = CreateUiObject("Settings Title Row", parent);
            LayoutElement rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.minHeight = 26f;
            rowLayout.preferredHeight = 26f;

            HorizontalLayoutGroup group = row.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 8f;
            group.childAlignment = TextAnchor.MiddleLeft;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = false;
            group.childForceExpandWidth = false;
            return row.transform;
        }

        private Transform CreateUsageGuideHeader(Transform parent)
        {
            GameObject row = CreateUiObject("Usage Guide Header", parent);
            LayoutElement rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.minHeight = 40f;
            rowLayout.preferredHeight = 40f;

            HorizontalLayoutGroup group = row.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 10f;
            group.childAlignment = TextAnchor.MiddleLeft;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = false;
            group.childForceExpandWidth = false;
            return row.transform;
        }

        private Transform CreateUsageGuideScrollView(Transform parent)
        {
            GameObject scrollViewObject = CreateUiObject("Usage Guide Scroll View", parent);
            Image scrollBackground = scrollViewObject.AddComponent<Image>();
            scrollBackground.color = new Color32(10, 15, 22, 255);
            Outline scrollOutline = scrollViewObject.AddComponent<Outline>();
            scrollOutline.effectColor = new Color32(56, 74, 94, 130);
            scrollOutline.effectDistance = new Vector2(1f, -1f);
            LayoutElement scrollLayout = scrollViewObject.AddComponent<LayoutElement>();
            scrollLayout.minHeight = 300f;
            scrollLayout.flexibleHeight = 1f;
            scrollLayout.flexibleWidth = 1f;

            ScrollRect scrollRect = scrollViewObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Elastic;
            scrollRect.inertia = true;
            scrollRect.scrollSensitivity = 28f;

            GameObject viewport = CreateUiObject("Viewport", scrollViewObject.transform);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            Stretch(viewportRect);
            viewportRect.offsetMin = new Vector2(1f, 1f);
            viewportRect.offsetMax = new Vector2(-1f, -1f);
            Image viewportImage = viewport.AddComponent<Image>();
            viewportImage.color = new Color32(10, 15, 22, 255);
            Mask mask = viewport.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            GameObject content = CreateUiObject("Content", viewport.transform);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(12, 12, 12, 18);
            contentLayout.childAlignment = TextAnchor.UpperLeft;
            contentLayout.childControlHeight = true;
            contentLayout.childControlWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.childForceExpandWidth = true;
            ContentSizeFitter contentFitter = content.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            return content.transform;
        }

        private TMP_Text AddQuoteText(Transform parent, string text)
        {
            TMP_Text label = AddText(parent, text, 15, FontStyles.Bold, new Color32(244, 247, 251, 255));
            label.alignment = TextAlignmentOptions.TopLeft;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.enableAutoSizing = true;
            label.fontSizeMin = 10;
            label.fontSizeMax = 15;
            LayoutElement layout = label.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 36;
            layout.preferredHeight = 36;
            return label;
        }

        private TMP_Text AddValueRow(Transform parent, string label, string value)
        {
            GameObject field = CreateCompactField(parent, label);
            AddCompactFieldLabel(field.transform, label);

            GameObject valueObject = CreateUiObject(label + " Value", field.transform);
            Image valueBackground = valueObject.AddComponent<Image>();
            valueBackground.color = new Color32(14, 16, 19, 255);
            TMP_Text valueText = AddText(valueObject.transform, value, 12, FontStyles.Bold, new Color32(238, 242, 247, 255));
            valueText.alignment = TextAlignmentOptions.Center;
            Stretch(valueText.GetComponent<RectTransform>());
            LayoutElement valueLayout = valueObject.AddComponent<LayoutElement>();
            valueLayout.minHeight = 32;
            valueLayout.preferredHeight = 32;
            return valueText;
        }

        private TMP_Text AddSectionTitle(Transform parent, string text)
        {
            TMP_Text label = AddText(parent, text, 13, FontStyles.Bold, new Color32(123, 217, 171, 255));
            LayoutElement layout = label.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 18;
            layout.preferredHeight = 18;
            return label;
        }

        private TMP_Text AddCompactInfoText(Transform parent, string text)
        {
            TMP_Text label = AddText(parent, text, 11, FontStyles.Normal, new Color32(170, 178, 190, 255));
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.enableAutoSizing = true;
            label.fontSizeMin = 8;
            label.fontSizeMax = 11;
            LayoutElement layout = label.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 18;
            layout.preferredHeight = 18;
            return label;
        }

        private TMP_Text AddBodyText(Transform parent, string text)
        {
            TMP_Text label = AddText(parent, text, 12, FontStyles.Normal, new Color32(211, 218, 228, 255));
            label.alignment = TextAlignmentOptions.TopLeft;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.enableAutoSizing = true;
            label.fontSizeMin = 8;
            label.fontSizeMax = 12;
            LayoutElement layout = label.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 42;
            layout.preferredHeight = 42;
            layout.flexibleWidth = 1;
            return label;
        }

        private Transform CreateCompactSection(Transform parent, string title)
        {
            GameObject section = CreateUiObject(title + " Section", parent);
            LayoutElement sectionLayout = section.AddComponent<LayoutElement>();
            sectionLayout.minHeight = 68;
            sectionLayout.preferredHeight = 68;

            VerticalLayoutGroup sectionGroup = section.AddComponent<VerticalLayoutGroup>();
            sectionGroup.spacing = 2;
            sectionGroup.childControlHeight = true;
            sectionGroup.childControlWidth = true;
            sectionGroup.childForceExpandHeight = false;
            sectionGroup.childForceExpandWidth = true;

            AddSectionTitle(section.transform, title);
            return CreateCompactFieldRow(section.transform, title + " Fields");
        }

        private Transform CreateCompactFieldRow(Transform parent, string name)
        {
            GameObject row = CreateUiObject(name, parent);
            LayoutElement rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.minHeight = 48;
            rowLayout.preferredHeight = 48;

            HorizontalLayoutGroup group = row.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 4;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = true;
            group.childForceExpandWidth = true;
            return row.transform;
        }

        private GameObject CreateCompactField(Transform parent, string label)
        {
            GameObject field = CreateUiObject(label + " Field", parent);
            LayoutElement fieldLayout = field.AddComponent<LayoutElement>();
            fieldLayout.flexibleWidth = 1;
            fieldLayout.minWidth = 0;

            VerticalLayoutGroup group = field.AddComponent<VerticalLayoutGroup>();
            group.spacing = 1;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = false;
            group.childForceExpandWidth = true;
            return field;
        }

        private TMP_Text AddCompactFieldLabel(Transform parent, string label)
        {
            TMP_Text labelText = AddText(parent, label, 10, FontStyles.Normal, new Color32(207, 214, 224, 255));
            labelText.overflowMode = TextOverflowModes.Ellipsis;
            labelText.enableAutoSizing = true;
            labelText.fontSizeMin = 8;
            labelText.fontSizeMax = 10;
            LayoutElement labelLayout = labelText.gameObject.AddComponent<LayoutElement>();
            labelLayout.minHeight = 15;
            labelLayout.preferredHeight = 15;
            return labelText;
        }

        private static TMP_FontAsset CreateChineseUiFont()
        {
            TMP_FontAsset bundledFontAsset = CreateBundledChineseUiFont();
            if (CanRenderChinese(bundledFontAsset))
            {
                return bundledFontAsset;
            }

            DestroyRuntimeFontAsset(bundledFontAsset);

            for (int i = 0; i < ChineseSystemFontCandidates.GetLength(0); i++)
            {
                string familyName = ChineseSystemFontCandidates[i, 0];
                string styleName = ChineseSystemFontCandidates[i, 1];
                TMP_FontAsset candidate = TMP_FontAsset.CreateFontAsset(familyName, styleName, DynamicFontSize);
                if (CanRenderChinese(candidate))
                {
                    candidate.name = string.IsNullOrEmpty(styleName)
                        ? $"{familyName} Dynamic TMP"
                        : $"{familyName} {styleName} Dynamic TMP";
                    return candidate;
                }

                DestroyRuntimeFontAsset(candidate);
            }

            TMP_Settings settings = TMP_Settings.LoadDefaultSettings();
            TMP_FontAsset defaultFont = settings == null ? null : TMP_Settings.defaultFontAsset;
            if (CanRenderChinese(defaultFont))
            {
                return defaultFont;
            }

            Font legacyFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return legacyFont == null ? null : TMP_FontAsset.CreateFontAsset(legacyFont);
        }

        private static TMP_FontAsset CreateBundledChineseUiFont()
        {
            Font bundledFont = Resources.Load<Font>(BundledChineseFontResourcePath);
            if (bundledFont == null)
            {
                return null;
            }

            TMP_FontAsset bundledFontAsset = TMP_FontAsset.CreateFontAsset(
                bundledFont,
                DynamicFontSize,
                9,
                GlyphRenderMode.SDFAA,
                1024,
                1024,
                AtlasPopulationMode.Dynamic,
                true);
            if (bundledFontAsset != null)
            {
                bundledFontAsset.name = "NotoSansSC Bundled Dynamic TMP";
            }

            return bundledFontAsset;
        }

        private static void DestroyRuntimeFontAsset(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null)
            {
                return;
            }

            if (fontAsset.material != null)
            {
                DestroyRuntimeObject(fontAsset.material);
            }

            Texture2D[] atlasTextures = fontAsset.atlasTextures;
            for (int i = 0; i < atlasTextures.Length; i++)
            {
                if (atlasTextures[i] != null)
                {
                    DestroyRuntimeObject(atlasTextures[i]);
                }
            }

            DestroyRuntimeObject(fontAsset);
        }

        private static void DestroyRuntimeObject(UnityEngine.Object target)
        {
            if (Application.isPlaying)
            {
                Destroy(target);
                return;
            }

            DestroyImmediate(target);
        }

        private static bool CanRenderChinese(TMP_FontAsset candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            return candidate.TryAddCharacters(ChineseFontProbeText, out string missingCharacters, true) &&
                   string.IsNullOrEmpty(missingCharacters);
        }

        private TMP_Text AddText(Transform parent, string value, int size, FontStyles style, Color32 color)
        {
            GameObject textObject = CreateUiObject("Text", parent);
            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                text.font = font;
            }
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private TMP_InputField AddInput(
            Transform parent,
            string label,
            string defaultValue,
            bool password = false,
            TMP_InputField.ContentType contentType = TMP_InputField.ContentType.DecimalNumber)
        {
            GameObject field = CreateCompactField(parent, label);
            AddCompactFieldLabel(field.transform, label);

            GameObject inputObject = CreateUiObject(label + " Input", field.transform);
            Image inputBackground = inputObject.AddComponent<Image>();
            inputBackground.color = new Color32(14, 16, 19, 255);
            TMP_Text inputText = CreateInputText(inputObject.transform, defaultValue);

            TMP_InputField input = inputObject.AddComponent<TMP_InputField>();
            input.targetGraphic = inputBackground;
            input.textComponent = inputText;
            input.textViewport = inputText.rectTransform;
            input.placeholder = null;
            input.contentType = password ? TMP_InputField.ContentType.Password : contentType;
            input.keyboardType = TouchScreenKeyboardType.NumbersAndPunctuation;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.SetTextWithoutNotify(defaultValue);

            LayoutElement inputLayout = inputObject.AddComponent<LayoutElement>();
            inputLayout.flexibleWidth = 1;
            inputLayout.minHeight = 32;
            inputLayout.preferredHeight = 32;
            return input;
        }

        private TMP_Text CreateInputText(Transform parent, string value)
        {
            GameObject textObject = CreateUiObject("Input Text", parent);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(6, 2);
            rect.offsetMax = new Vector2(-6, -2);

            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                text.font = font;
            }
            text.fontSize = 12;
            text.color = new Color32(238, 242, 247, 255);
            text.text = value;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private TMP_Dropdown AddDropdown(Transform parent, string label, List<string> options, int value)
        {
            GameObject field = CreateCompactField(parent, label);
            AddCompactFieldLabel(field.transform, label);

            GameObject dropdownObject = CreateUiObject(label + " Dropdown", field.transform);
            Image background = dropdownObject.AddComponent<Image>();
            background.color = new Color32(14, 16, 19, 255);
            TMP_Dropdown dropdown = dropdownObject.AddComponent<TMP_Dropdown>();
            dropdown.targetGraphic = background;
            dropdown.options = options.ConvertAll(option => new TMP_Dropdown.OptionData(option));
            dropdown.value = value;
            dropdown.captionText = AddText(dropdownObject.transform, options[value], 12, FontStyles.Normal, new Color32(238, 242, 247, 255));
            RectTransform captionRect = dropdown.captionText.GetComponent<RectTransform>();
            captionRect.anchorMin = Vector2.zero;
            captionRect.anchorMax = Vector2.one;
            captionRect.offsetMin = new Vector2(6, 2);
            captionRect.offsetMax = new Vector2(-6, -2);
            CreateDropdownTemplate(dropdown, dropdownObject.transform);

            LayoutElement dropdownLayout = dropdownObject.AddComponent<LayoutElement>();
            dropdownLayout.flexibleWidth = 1;
            dropdownLayout.minHeight = 32;
            dropdownLayout.preferredHeight = 32;
            return dropdown;
        }

        private void CreateDropdownTemplate(TMP_Dropdown dropdown, Transform parent)
        {
            GameObject template = CreateUiObject("Template", parent);
            template.SetActive(false);
            RectTransform templateRect = template.GetComponent<RectTransform>();
            templateRect.anchorMin = new Vector2(0f, 0f);
            templateRect.anchorMax = new Vector2(1f, 0f);
            templateRect.pivot = new Vector2(0.5f, 1f);
            templateRect.anchoredPosition = new Vector2(0f, -2f);
            templateRect.sizeDelta = new Vector2(0f, 108f);
            Image templateImage = template.AddComponent<Image>();
            templateImage.color = new Color32(18, 21, 25, 255);
            ScrollRect scrollRect = template.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;

            GameObject viewport = CreateUiObject("Viewport", template.transform);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            Stretch(viewportRect);
            Image viewportImage = viewport.AddComponent<Image>();
            viewportImage.color = new Color32(18, 21, 25, 255);
            Mask mask = viewport.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            GameObject content = CreateUiObject("Content", viewport.transform);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 96f);
            VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
            contentLayout.childControlHeight = true;
            contentLayout.childControlWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.childForceExpandWidth = true;
            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject item = CreateUiObject("Item", content.transform);
            RectTransform itemRect = item.GetComponent<RectTransform>();
            itemRect.sizeDelta = new Vector2(0f, 32f);
            LayoutElement itemLayout = item.AddComponent<LayoutElement>();
            itemLayout.minHeight = 32f;
            itemLayout.preferredHeight = 32f;
            Image itemBackground = item.AddComponent<Image>();
            itemBackground.color = new Color32(24, 28, 32, 255);
            Toggle itemToggle = item.AddComponent<Toggle>();
            itemToggle.targetGraphic = itemBackground;

            TMP_Text itemText = AddText(item.transform, "选项", 12, FontStyles.Normal, new Color32(238, 242, 247, 255));
            RectTransform itemTextRect = itemText.GetComponent<RectTransform>();
            itemTextRect.anchorMin = Vector2.zero;
            itemTextRect.anchorMax = Vector2.one;
            itemTextRect.offsetMin = new Vector2(6f, 0f);
            itemTextRect.offsetMax = new Vector2(-6f, 0f);

            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            dropdown.template = templateRect;
            dropdown.itemText = itemText;
        }

        private Toggle AddToggle(Transform parent, string label, bool defaultValue)
        {
            GameObject field = CreateCompactField(parent, label);
            AddCompactFieldLabel(field.transform, label);

            GameObject box = CreateUiObject("Toggle Box", field.transform);
            Image boxImage = box.AddComponent<Image>();
            boxImage.color = new Color32(14, 16, 19, 255);
            LayoutElement boxLayout = box.AddComponent<LayoutElement>();
            boxLayout.minHeight = 32;
            boxLayout.preferredHeight = 32;

            GameObject checkmark = CreateUiObject("Checkmark", box.transform);
            Image checkmarkImage = checkmark.AddComponent<Image>();
            checkmarkImage.color = new Color32(123, 217, 171, 255);
            RectTransform checkmarkRect = checkmark.GetComponent<RectTransform>();
            checkmarkRect.anchorMin = new Vector2(0.35f, 0.25f);
            checkmarkRect.anchorMax = new Vector2(0.65f, 0.75f);
            checkmarkRect.offsetMin = Vector2.zero;
            checkmarkRect.offsetMax = Vector2.zero;

            Toggle toggle = field.AddComponent<Toggle>();
            toggle.targetGraphic = boxImage;
            toggle.graphic = checkmarkImage;
            toggle.isOn = defaultValue;
            return toggle;
        }

        private Button AddButton(Transform parent, string label, string buttonText = null)
        {
            GameObject field = CreateCompactField(parent, label);
            AddCompactFieldLabel(field.transform, label);

            GameObject buttonObject = CreateUiObject(label + " Button", field.transform);
            Image background = buttonObject.AddComponent<Image>();
            background.color = new Color32(55, 132, 93, 255);
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;
            TMP_Text text = AddText(buttonObject.transform, string.IsNullOrWhiteSpace(buttonText) ? label : buttonText, 11, FontStyles.Bold, Color.white);
            text.alignment = TextAlignmentOptions.Center;
            RectTransform textRect = text.GetComponent<RectTransform>();
            Stretch(textRect);

            LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
            layout.minHeight = 32;
            layout.preferredHeight = 32;
            return button;
        }

        private Button AddAdviceResultButton(Transform parent, out TMP_Text buttonText)
        {
            GameObject buttonObject = CreateUiObject("Advice Result Button", parent);
            Image background = buttonObject.AddComponent<Image>();
            background.color = new Color32(24, 74, 62, 255);
            Outline outline = buttonObject.AddComponent<Outline>();
            outline.effectColor = new Color32(123, 217, 171, 145);
            outline.effectDistance = new Vector2(1f, -1f);

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color32(180, 242, 211, 255);
            colors.pressedColor = new Color32(134, 196, 167, 255);
            colors.disabledColor = new Color32(104, 110, 117, 190);
            button.colors = colors;

            GameObject marker = CreateUiObject("Advice Marker", buttonObject.transform);
            RectTransform markerRect = marker.GetComponent<RectTransform>();
            markerRect.anchorMin = new Vector2(0f, 0.18f);
            markerRect.anchorMax = new Vector2(0f, 0.82f);
            markerRect.pivot = new Vector2(0f, 0.5f);
            markerRect.offsetMin = new Vector2(0f, 0f);
            markerRect.offsetMax = new Vector2(4f, 0f);
            Image markerImage = marker.AddComponent<Image>();
            markerImage.color = new Color32(123, 217, 171, 255);
            markerImage.raycastTarget = false;

            buttonText = AddText(
                buttonObject.transform,
                "暂无 AI 建议",
                12,
                FontStyles.Bold,
                new Color32(233, 244, 239, 255));
            buttonText.gameObject.name = "Advice Result Button Text";
            buttonText.alignment = TextAlignmentOptions.MidlineLeft;
            RectTransform textRect = buttonText.GetComponent<RectTransform>();
            Stretch(textRect);
            textRect.offsetMin = new Vector2(14f, 0f);
            textRect.offsetMax = new Vector2(-38f, 0f);

            TMP_Text arrow = AddText(
                buttonObject.transform,
                "›",
                22,
                FontStyles.Normal,
                new Color32(123, 217, 171, 255));
            arrow.gameObject.name = "Advice Result Arrow";
            arrow.alignment = TextAlignmentOptions.Center;
            RectTransform arrowRect = arrow.GetComponent<RectTransform>();
            arrowRect.anchorMin = new Vector2(1f, 0f);
            arrowRect.anchorMax = Vector2.one;
            arrowRect.pivot = new Vector2(1f, 0.5f);
            arrowRect.offsetMin = new Vector2(-34f, 0f);
            arrowRect.offsetMax = new Vector2(-4f, 0f);

            LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
            layout.minHeight = 40f;
            layout.preferredHeight = 40f;
            layout.flexibleWidth = 1f;
            return button;
        }

        private Button AddToolbarButton(
            Transform parent,
            string buttonName,
            string iconName,
            Sprite iconSprite,
            string fallbackText)
        {
            GameObject buttonObject = CreateUiObject(buttonName, parent);
            Image background = buttonObject.AddComponent<Image>();
            background.color = new Color32(13, 19, 27, 255);
            Outline outline = buttonObject.AddComponent<Outline>();
            outline.effectColor = new Color32(49, 181, 233, 150);
            outline.effectDistance = new Vector2(1f, -1f);

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color32(27, 52, 68, 255);
            colors.pressedColor = new Color32(10, 28, 39, 255);
            button.colors = colors;

            if (iconSprite != null)
            {
                GameObject iconObject = CreateUiObject(iconName, buttonObject.transform);
                RectTransform iconRect = iconObject.GetComponent<RectTransform>();
                Stretch(iconRect);
                iconRect.offsetMin = new Vector2(7f, 7f);
                iconRect.offsetMax = new Vector2(-7f, -7f);
                Image icon = iconObject.AddComponent<Image>();
                icon.sprite = iconSprite;
                icon.color = Color.white;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
            }
            else
            {
                TMP_Text text = AddText(
                    buttonObject.transform,
                    fallbackText,
                    9,
                    FontStyles.Bold,
                    new Color32(223, 229, 237, 255));
                text.alignment = TextAlignmentOptions.Center;
                Stretch(text.GetComponent<RectTransform>());
            }

            LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
            layout.minWidth = 36f;
            layout.preferredWidth = 36f;
            layout.minHeight = 36f;
            layout.preferredHeight = 36f;
            return button;
        }

        private Button AddSettingsMenuButton(Transform parent, string buttonText)
        {
            GameObject buttonObject = CreateUiObject("Usage Guide Button", parent);
            Image background = buttonObject.AddComponent<Image>();
            background.color = new Color32(18, 28, 39, 255);
            Outline outline = buttonObject.AddComponent<Outline>();
            outline.effectColor = new Color32(49, 181, 233, 135);
            outline.effectDistance = new Vector2(1f, -1f);

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color32(27, 52, 68, 255);
            colors.pressedColor = new Color32(12, 23, 32, 255);
            button.colors = colors;

            TMP_Text text = AddText(
                buttonObject.transform,
                buttonText,
                12,
                FontStyles.Bold,
                new Color32(222, 231, 241, 255));
            text.alignment = TextAlignmentOptions.MidlineLeft;
            RectTransform textRect = text.GetComponent<RectTransform>();
            Stretch(textRect);
            textRect.offsetMin = new Vector2(12f, 0f);
            textRect.offsetMax = new Vector2(-36f, 0f);

            TMP_Text arrow = AddText(
                buttonObject.transform,
                "›",
                20,
                FontStyles.Normal,
                new Color32(49, 181, 233, 255));
            arrow.alignment = TextAlignmentOptions.Center;
            RectTransform arrowRect = arrow.GetComponent<RectTransform>();
            arrowRect.anchorMin = new Vector2(1f, 0f);
            arrowRect.anchorMax = Vector2.one;
            arrowRect.pivot = new Vector2(1f, 0.5f);
            arrowRect.offsetMin = new Vector2(-34f, 0f);
            arrowRect.offsetMax = new Vector2(-4f, 0f);

            LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
            layout.minHeight = 36f;
            layout.preferredHeight = 36f;
            return button;
        }

        private Button AddUsageGuideBackButton(Transform parent, string buttonText)
        {
            GameObject buttonObject = CreateUiObject("Back Button", parent);
            Image background = buttonObject.AddComponent<Image>();
            background.color = new Color32(13, 19, 27, 255);
            Outline outline = buttonObject.AddComponent<Outline>();
            outline.effectColor = new Color32(49, 181, 233, 135);
            outline.effectDistance = new Vector2(1f, -1f);

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;
            TMP_Text text = AddText(
                buttonObject.transform,
                buttonText,
                11,
                FontStyles.Bold,
                new Color32(222, 231, 241, 255));
            text.alignment = TextAlignmentOptions.Center;
            Stretch(text.GetComponent<RectTransform>());

            LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
            layout.minWidth = 62f;
            layout.preferredWidth = 62f;
            layout.minHeight = 34f;
            layout.preferredHeight = 34f;
            return button;
        }

        private Button AddModalButton(Transform parent, string buttonText)
        {
            GameObject buttonObject = CreateUiObject("Close Settings Button", parent);
            Image background = buttonObject.AddComponent<Image>();
            background.color = new Color32(55, 132, 93, 255);
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;

            TMP_Text text = AddText(buttonObject.transform, buttonText, 11, FontStyles.Bold, Color.white);
            text.alignment = TextAlignmentOptions.Center;
            Stretch(text.GetComponent<RectTransform>());

            LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
            layout.minHeight = 32f;
            layout.preferredHeight = 32f;
            return button;
        }

        private void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null)
            {
                return;
            }

            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            eventSystem.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            eventSystem.AddComponent<StandaloneInputModule>();
#endif
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
