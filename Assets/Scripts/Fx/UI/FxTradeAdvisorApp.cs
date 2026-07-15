using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestFXTrade.Fx.Analysis;
using TestFXTrade.Fx.Domain;
using TestFXTrade.Fx.MarketData;
using TestFXTrade.Fx.OpenAI;
using TestFXTrade.Fx.Sbi;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TestFXTrade.Fx.UI
{
    [DefaultExecutionOrder(-500)]
    public sealed class FxTradeAdvisorApp : MonoBehaviour
    {
        private const float QuoteRefreshSeconds = 5f;
        private const float CandleRefreshSeconds = 60f;
        private const int CandleOutputSize = 160;
        private static readonly Vector2 MobileReferenceResolution = new Vector2(390f, 844f);
        private static readonly string[] ChineseFontNames =
        {
            "PingFang SC",
            "Heiti SC",
            "STHeiti",
            "Noto Sans CJK SC",
            "Noto Sans SC",
            "Microsoft YaHei",
            "Droid Sans Fallback",
            "Arial Unicode MS"
        };

        private Font font;
        private InputField principalInput;
        private InputField netPositionInput;
        private Dropdown intervalDropdown;
        private Toggle autoRefreshToggle;
        private Button syncSbiRulesButton;
        private Button requestAiAdviceButton;
        private Button aggressiveAiAdviceButton;
        private Text marketSourceText;
        private Text statusText;
        private Text sbiRulesText;
        private Text quoteText;
        private Text metricsText;
        private Text recommendationText;
        private Text warningsText;
        private UsdJpyTrendLineGraphic chartGraphic;
        private CanvasScaler canvasScaler;
        private RectTransform safeAreaContentRect;
        private CanvasGroup contentCanvasGroup;
        private GameObject loadingOverlay;
        private Text loadingTaskText;
        private RectTransform loadingSpinnerRect;
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
        private float nextQuoteRefreshAt;
        private float nextCandleRefreshAt;
        private bool quoteRefreshInFlight;
        private bool candleRefreshInFlight;
        private bool sbiSyncInFlight;
        private bool aiAdviceInFlight;
        private int loadingOperationCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<FxTradeAdvisorApp>() != null)
            {
                return;
            }

            GameObject app = new GameObject("USDJPY Trade Advisor App");
            app.AddComponent<FxTradeAdvisorApp>();
        }

        private void Awake()
        {
            ConfigurePortraitRuntime();

            font = Font.CreateDynamicFontFromOSFont(ChineseFontNames, 16);
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            BuildUi();
        }

        private void Start()
        {
            AzureRelaySettings settings = AzureRelaySettings.Load();
            relayBaseUrl = settings.BaseUrl;
            sbiRules = sbiRuleService.LoadLocal();
            RenderSbiRuleState();
            RenderRelayState(settings.SourceLabel);

            if (settings.IsConfigured)
            {
                marketDataProvider = new AzureRelayMarketDataProvider(relayBaseUrl);
                openAiClient = new AzureRelayTradeAdvisorClient(relayBaseUrl);
                SetStatus("已连接 Azure 中转，正在刷新 USD/JPY 实时行情。");
                nextQuoteRefreshAt = Time.time + QuoteRefreshSeconds;
                nextCandleRefreshAt = Time.time + CandleRefreshSeconds;
                SetWarning("供应商密钥仅保存在后台。AI建议仅供参考，不构成投资建议。");
                _ = RefreshCandlesAndQuoteAsync(true);
                return;
            }

            SetStatus("尚未配置 Azure 中转地址。");
            SetWarning("请部署 azure-relay，并在 AzureRelayConfig.json 中填写 Function App 地址。");
            nextQuoteRefreshAt = float.PositiveInfinity;
            nextCandleRefreshAt = float.PositiveInfinity;
        }

        private void Update()
        {
            UpdateLoadingAnimation();
            RefreshAdaptiveLayout();

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
        }

        private void BuildUi()
        {
            EnsureEventSystem();

            Canvas canvas = CreateCanvas();
            GameObject root = CreateUiObject("Root", canvas.transform);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            Stretch(rootRect);
            Image rootBackground = root.AddComponent<Image>();
            rootBackground.color = new Color32(18, 20, 23, 255);

            GameObject content = CreateUiObject("Safe Area Content", root.transform);
            safeAreaContentRect = content.GetComponent<RectTransform>();
            Stretch(safeAreaContentRect);
            contentCanvasGroup = content.AddComponent<CanvasGroup>();

            VerticalLayoutGroup contentGroup = content.AddComponent<VerticalLayoutGroup>();
            contentGroup.padding = new RectOffset(10, 10, 8, 8);
            contentGroup.spacing = 4;
            contentGroup.childControlWidth = true;
            contentGroup.childControlHeight = true;
            contentGroup.childForceExpandWidth = true;
            contentGroup.childForceExpandHeight = false;

            BuildMarketControls(content.transform);
            BuildInputColumn(content.transform);
            BuildOutputColumn(content.transform);
            BuildLoadingOverlay(root.transform);
            RefreshAdaptiveLayout(true);
        }

        private void BuildMarketControls(Transform parent)
        {
            AddHeader(parent, "USD/JPY 交易助手");
            marketSourceText = AddCompactInfoText(parent, "正在读取 Azure 中转配置……");

            Transform controls = CreateCompactFieldRow(parent, "Market Controls");
            AddValueRow(controls, "交易对", FxConstants.UsdJpySymbol);
            intervalDropdown = AddDropdown(controls, "周期", new List<string> { "1min", "5min", "15min" }, 1);
            intervalDropdown.onValueChanged.AddListener(unused => { _ = RefreshCandlesAndQuoteAsync(true); });
            autoRefreshToggle = AddToggle(controls, "实时 5秒", true);

            Button refreshButton = AddButton(controls, "手动行情", "刷新");
            refreshButton.onClick.AddListener(() => _ = RefreshCandlesAndQuoteAsync(true));

            statusText = AddCompactInfoText(parent, string.Empty);
            statusText.color = new Color32(190, 198, 210, 255);
        }

        private void BuildInputColumn(Transform parent)
        {
            Transform smartFields = CreateCompactSection(parent, "智能建议");
            LayoutElement smartSectionLayout = smartFields.parent.GetComponent<LayoutElement>();
            smartSectionLayout.minHeight = 110;
            smartSectionLayout.preferredHeight = 110;

            principalInput = AddInput(smartFields, "本金 JPY", "1000000");
            netPositionInput = AddInput(smartFields, "净持仓 lots", "0");

            syncSbiRulesButton = AddButton(smartFields, "SBI规则", "同步");
            syncSbiRulesButton.onClick.AddListener(() => _ = SyncSbiRulesAsync());

            Transform adviceActions = CreateCompactFieldRow(smartFields.parent, "AI Advice Actions");
            requestAiAdviceButton = AddButton(adviceActions, "稳健建议", "获取");
            requestAiAdviceButton.onClick.AddListener(() => _ = RequestAiAdviceAsync(AiTradeAdviceMode.Conservative));

            aggressiveAiAdviceButton = AddButton(adviceActions, "积极建议", "买/卖必选");
            aggressiveAiAdviceButton.targetGraphic.color = new Color32(175, 101, 52, 255);
            aggressiveAiAdviceButton.onClick.AddListener(() => _ = RequestAiAdviceAsync(AiTradeAdviceMode.ForcedDirectional));

            sbiRulesText = AddCompactInfoText(parent, "SBI规则：尚未同步。");
        }

        private void BuildOutputColumn(Transform parent)
        {
            quoteText = AddQuoteText(parent, "正在等待 USD/JPY 实时行情");

            GameObject chartPanel = CreatePanel("ChartPanel", parent, new Color32(12, 14, 17, 255));
            LayoutElement chartLayout = chartPanel.AddComponent<LayoutElement>();
            chartLayout.minHeight = 56;
            chartLayout.preferredHeight = 60;
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

            metricsText = AddBodyText(parent, "行情指标将在此显示。");
            recommendationText = AddBodyText(parent, "输入本金和净持仓，同步SBI规则后即可获取AI建议。");
            recommendationText.gameObject.name = "AI Advice Text";
            LayoutElement recommendationLayout = recommendationText.GetComponent<LayoutElement>();
            recommendationLayout.minHeight = 104;
            recommendationLayout.preferredHeight = 108;
            recommendationLayout.flexibleHeight = 1;
            recommendationText.fontSize = 12;
            recommendationText.color = new Color32(234, 239, 246, 255);
            warningsText = AddBodyText(parent, string.Empty);
            LayoutElement warningsLayout = warningsText.GetComponent<LayoutElement>();
            warningsLayout.minHeight = 36;
            warningsLayout.preferredHeight = 36;
            warningsText.color = new Color32(244, 192, 102, 255);
        }

        private async Task RefreshCandlesAndQuoteAsync(bool manual)
        {
            if (quoteRefreshInFlight || candleRefreshInFlight)
            {
                if (manual)
                {
                    SetStatus("实时行情正在刷新中。");
                }

                return;
            }

            CancellationToken token = GetMarketDataToken();
            candleRefreshInFlight = true;
            quoteRefreshInFlight = true;
            BeginLoading("正在获取 USD/JPY 实时报价与K线");
            nextCandleRefreshAt = Time.time + CandleRefreshSeconds;
            nextQuoteRefreshAt = Time.time + QuoteRefreshSeconds;

            try
            {
                if (string.IsNullOrWhiteSpace(relayBaseUrl))
                {
                    SetStatus("尚未配置 Azure 中转地址。");
                    SetWarning("请检查 AzureRelayConfig.json。");
                    return;
                }

                string symbol = FxConstants.UsdJpySymbol;
                SetStatus($"正在获取 {symbol} 实时报价与K线……");

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
                RenderLatestMarketState(interval, $"{provider.ProviderName} 数据已更新：{DateTime.Now:HH:mm:ss}");
            }
            catch (OperationCanceledException)
            {
                SetStatus("已取消刷新。");
            }
            catch (Exception ex)
            {
                SetStatus("实时行情暂不可用。");
                SetWarning(ex.Message);
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
                    SetStatus("实时报价正在刷新中。");
                }

                return;
            }

            CancellationToken token = GetMarketDataToken();
            quoteRefreshInFlight = true;
            BeginLoading("正在更新 USD/JPY 实时报价");
            nextQuoteRefreshAt = Time.time + QuoteRefreshSeconds;

            try
            {
                if (string.IsNullOrWhiteSpace(relayBaseUrl))
                {
                    SetStatus("尚未配置 Azure 中转地址。");
                    SetWarning("请检查 AzureRelayConfig.json。");
                    return;
                }

                string symbol = FxConstants.UsdJpySymbol;
                SetStatus($"正在更新 {symbol} 实时报价……");

                IFxMarketDataProvider provider = GetMarketDataProvider();
                latestQuote = await provider.GetLatestQuoteAsync(symbol, token);

                if (token.IsCancellationRequested)
                {
                    return;
                }

                RenderLatestMarketState(GetSelectedInterval(), $"实时报价已更新：{DateTime.Now:HH:mm:ss}");
            }
            catch (OperationCanceledException)
            {
                SetStatus("已取消刷新。");
            }
            catch (Exception ex)
            {
                SetStatus("实时报价暂不可用。");
                SetWarning(ex.Message);
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

        private void RenderLatestMarketState(string interval, string statusMessage)
        {
            if (latestQuote == null)
            {
                SetStatus(statusMessage);
                return;
            }

            IReadOnlyList<Candle> realtimeCandles = BuildRealtimeCandles();
            chartGraphic.SetCandles(realtimeCandles);
            RenderMarketMetrics(latestQuote, realtimeCandles, interval);
            SetStatus(statusMessage);
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
                : "数据源时间戳不可用";

            quoteText.text = $"{quote.Symbol} {quote.Price:0.000}\n{freshness} | {interval} | {candles.Count} 根K线";

            double trendScore = TechnicalIndicatorService.CalculateTrendScore(candles, out double atrPips, out double rsi);
            double netPosition = ParseInput(netPositionInput, 0d);
            string positionLabel = netPosition > 0d ? "多" : netPosition < 0d ? "空" : "空仓";
            metricsText.text =
                $"趋势 {trendScore:0.00}   RSI {rsi:0.0}   ATR {atrPips:0.0} pips\n" +
                $"当前净持仓 {netPosition:0.###} lots（{positionLabel}）   1 lot = 100,000 通货";
        }

        private async Task SyncSbiRulesAsync()
        {
            if (sbiSyncInFlight)
            {
                return;
            }

            sbiSyncInFlight = true;
            syncSbiRulesButton.interactable = false;
            BeginLoading("正在同步 SBI FX 最新规则");
            CancellationToken token = GetAdvisoryToken();

            try
            {
                SetStatus("正在从SBI证券官方页面读取最新FX规则……");
                sbiRules = await sbiRuleService.RefreshAsync(token);
                RenderSbiRuleState();
                SetStatus($"SBI FX规则已保存到本地：{DateTime.Now:HH:mm:ss}");
                SetWarning("已采用官方25倍杠杆保证金表。AI建议仅供参考，不构成投资建议。");
            }
            catch (OperationCanceledException)
            {
                SetStatus("已取消SBI规则同步。");
            }
            catch (Exception ex)
            {
                SetStatus("SBI FX规则同步失败。");
                SetWarning(ex.Message);
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

            if (!TryReadAdvisorInputs(out double principalJpy, out double netPositionLots))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(relayBaseUrl) || openAiClient == null)
            {
                SetStatus("无法请求AI建议。");
                SetWarning("请先配置并部署 Azure 中转服务。");
                return;
            }

            if (sbiRules == null || !sbiRules.IsUsable)
            {
                SetStatus("请先同步SBI FX规则。");
                SetWarning("点击“SBI规则 / 同步”，成功保存官方规则后再获取AI建议。");
                return;
            }

            aiAdviceInFlight = true;
            SetAdviceButtonsInteractable(false);
            BeginLoading("正在更新行情并准备 AI 分析");
            CancellationToken token = GetAdvisoryToken();

            try
            {
                SetStatus("正在更新行情并准备AI分析……");
                while (quoteRefreshInFlight || candleRefreshInFlight)
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Yield();
                }

                await RefreshCandlesAndQuoteAsync(true);
                token.ThrowIfCancellationRequested();

                if (latestQuote == null || latestCandles.Count == 0)
                {
                    throw new InvalidOperationException("缺少实时行情，暂时不能生成AI建议。");
                }

                IReadOnlyList<Candle> realtimeCandles = BuildRealtimeCandles();
                string prompt = OpenAiTradePromptBuilder.Build(
                    principalJpy,
                    netPositionLots,
                    latestQuote,
                    realtimeCandles,
                    GetSelectedInterval(),
                    sbiRules,
                    mode);

                string modeLabel = mode == AiTradeAdviceMode.ForcedDirectional ? "积极策略" : "稳健策略";
                recommendationText.text = $"OpenAI正在按{modeLabel}结合实时行情、技术指标和SBI保证金规则进行分析……";
                SetStatus($"正在通过 Azure 请求 OpenAI（{modeLabel}）……");
                SetLoadingTask($"正在等待 OpenAI 返回{modeLabel}建议");
                OpenAiTradeAdvice advice = await openAiClient.GetAdviceAsync(prompt, mode, token);
                bool adjusted = ApplyLocalMarginGuard(advice, principalJpy, netPositionLots, sbiRules, mode);
                RenderAiAdvice(advice, principalJpy, netPositionLots, adjusted, mode);
                SetStatus($"{modeLabel}已更新：{DateTime.Now:HH:mm:ss}");
            }
            catch (OperationCanceledException)
            {
                SetStatus("已取消AI建议请求。");
            }
            catch (Exception ex)
            {
                SetStatus("AI建议请求失败。");
                recommendationText.text = "当前未取得新的AI建议，请保留原持仓判断并检查配置。";
                SetWarning(ex.Message);
            }
            finally
            {
                aiAdviceInFlight = false;
                EndLoading();
                SetAdviceButtonsInteractable(true);
            }
        }

        private void BuildLoadingOverlay(Transform parent)
        {
            loadingOverlay = CreateUiObject("Loading Overlay", parent);
            RectTransform overlayRect = loadingOverlay.GetComponent<RectTransform>();
            Stretch(overlayRect);

            Image backdrop = loadingOverlay.AddComponent<Image>();
            backdrop.color = new Color32(7, 9, 11, 224);
            backdrop.raycastTarget = true;

            GameObject panel = CreateUiObject("Loading Panel", loadingOverlay.transform);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(310f, 126f);
            Image panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color32(25, 29, 34, 255);
            panelImage.raycastTarget = false;

            GameObject accent = CreateUiObject("Accent", panel.transform);
            RectTransform accentRect = accent.GetComponent<RectTransform>();
            accentRect.anchorMin = new Vector2(0f, 1f);
            accentRect.anchorMax = new Vector2(1f, 1f);
            accentRect.pivot = new Vector2(0.5f, 1f);
            accentRect.sizeDelta = new Vector2(0f, 3f);
            Image accentImage = accent.AddComponent<Image>();
            accentImage.color = new Color32(123, 217, 171, 255);
            accentImage.raycastTarget = false;

            GameObject spinner = CreateUiObject("Loading Spinner", panel.transform);
            loadingSpinnerRect = spinner.GetComponent<RectTransform>();
            loadingSpinnerRect.anchorMin = new Vector2(0.5f, 0.5f);
            loadingSpinnerRect.anchorMax = new Vector2(0.5f, 0.5f);
            loadingSpinnerRect.pivot = new Vector2(0.5f, 0.5f);
            loadingSpinnerRect.anchoredPosition = new Vector2(-119f, 0f);
            loadingSpinnerRect.sizeDelta = new Vector2(44f, 44f);
            CreateLoadingSpinnerDots(spinner.transform);

            Text title = AddText(panel.transform, "处理中", 12, FontStyle.Bold, new Color32(123, 217, 171, 255));
            RectTransform titleRect = title.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0.5f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(62f, 0f);
            titleRect.offsetMax = new Vector2(-16f, -18f);
            title.alignment = TextAnchor.LowerLeft;
            title.raycastTarget = false;

            loadingTaskText = AddText(panel.transform, string.Empty, 14, FontStyle.Bold, new Color32(239, 243, 248, 255));
            RectTransform taskRect = loadingTaskText.GetComponent<RectTransform>();
            taskRect.anchorMin = new Vector2(0f, 0f);
            taskRect.anchorMax = new Vector2(1f, 0.5f);
            taskRect.offsetMin = new Vector2(62f, 18f);
            taskRect.offsetMax = new Vector2(-16f, 0f);
            loadingTaskText.alignment = TextAnchor.UpperLeft;
            loadingTaskText.verticalOverflow = VerticalWrapMode.Truncate;
            loadingTaskText.resizeTextForBestFit = true;
            loadingTaskText.resizeTextMinSize = 10;
            loadingTaskText.resizeTextMaxSize = 14;
            loadingTaskText.raycastTarget = false;

            loadingOverlay.SetActive(false);
        }

        private void CreateLoadingSpinnerDots(Transform parent)
        {
            const int dotCount = 8;
            const float radius = 17f;
            for (int i = 0; i < dotCount; i++)
            {
                float angle = (Mathf.PI * 2f * i) / dotCount;
                GameObject dot = CreateUiObject($"Dot {i + 1}", parent);
                RectTransform dotRect = dot.GetComponent<RectTransform>();
                dotRect.anchorMin = new Vector2(0.5f, 0.5f);
                dotRect.anchorMax = new Vector2(0.5f, 0.5f);
                dotRect.pivot = new Vector2(0.5f, 0.5f);
                dotRect.anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * radius;
                dotRect.sizeDelta = new Vector2(5f, 8f);
                dotRect.localRotation = Quaternion.Euler(0f, 0f, -angle * Mathf.Rad2Deg);

                Image dotImage = dot.AddComponent<Image>();
                float alpha = Mathf.Lerp(0.28f, 1f, (i + 1f) / dotCount);
                dotImage.color = new Color(0.48f, 0.85f, 0.67f, alpha);
                dotImage.raycastTarget = false;
            }
        }

        private void BeginLoading(string task)
        {
            loadingOperationCount++;
            SetLoadingTask(task);

            if (loadingOperationCount != 1)
            {
                return;
            }

            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }

            if (contentCanvasGroup != null)
            {
                contentCanvasGroup.interactable = false;
                contentCanvasGroup.blocksRaycasts = false;
            }

            if (loadingOverlay != null)
            {
                loadingOverlay.SetActive(true);
                loadingOverlay.transform.SetAsLastSibling();
            }
        }

        private void SetLoadingTask(string task)
        {
            if (loadingTaskText != null)
            {
                loadingTaskText.text = string.IsNullOrWhiteSpace(task) ? "正在处理请求" : task;
            }
        }

        private void EndLoading()
        {
            loadingOperationCount = Mathf.Max(0, loadingOperationCount - 1);
            if (loadingOperationCount > 0)
            {
                return;
            }

            if (loadingOverlay != null)
            {
                loadingOverlay.SetActive(false);
            }

            if (contentCanvasGroup != null)
            {
                contentCanvasGroup.interactable = true;
                contentCanvasGroup.blocksRaycasts = true;
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

        private bool TryReadAdvisorInputs(out double principalJpy, out double netPositionLots)
        {
            principalJpy = 0d;
            netPositionLots = 0d;

            if (principalInput == null ||
                !double.TryParse(principalInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out principalJpy) ||
                double.IsNaN(principalJpy) ||
                double.IsInfinity(principalJpy) ||
                principalJpy <= 0d)
            {
                SetStatus("输入内容有误。");
                SetWarning("本金必须是大于0的JPY金额。");
                return false;
            }

            if (netPositionInput == null ||
                !double.TryParse(netPositionInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out netPositionLots) ||
                double.IsNaN(netPositionLots) ||
                double.IsInfinity(netPositionLots))
            {
                SetStatus("输入内容有误。");
                SetWarning("净持仓必须是数字：正数为多头，负数为空头，0为空仓。");
                return false;
            }

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
                    advice.summary = "原方向超出保证金限制，改为反向小额交易";
                    advice.reasoning = "模型原方向在本地保证金上限下不可执行，因此选择可执行的反方向最小单。";
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
                    ? "建议方向或手数已由本地保证金保护规则调整。"
                    : advice.risk_warning + " 本地保证金保护规则已调整建议方向或手数。";
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
            string actionLabel;
            double postTradeNetLots = currentNetLots;
            switch (advice.action)
            {
                case "BUY":
                    actionLabel = "买入";
                    postTradeNetLots += advice.suggested_lots;
                    break;
                case "SELL":
                    actionLabel = "卖出";
                    postTradeNetLots -= advice.suggested_lots;
                    break;
                default:
                    actionLabel = "观望";
                    break;
            }

            double postTradeMargin = Math.Abs(postTradeNetLots) * sbiRules.RequiredMarginPerStandardLotJpy;
            double marginUsagePercent = principalJpy > 0d ? (postTradeMargin / principalJpy) * 100d : 0d;
            string modeLabel = mode == AiTradeAdviceMode.ForcedDirectional ? "积极建议" : "稳健建议";
            StringBuilder adviceText = new StringBuilder();
            adviceText.AppendLine($"{modeLabel}：{actionLabel} {advice.suggested_lots:0.###} lot   置信度 {advice.confidence:P0}");
            adviceText.AppendLine(advice.summary);
            adviceText.Append($"依据：{advice.reasoning}");
            recommendationText.text = adviceText.ToString();

            string adjustedText = adjusted ? " 已执行本地手数保护。" : string.Empty;
            string forcedDirectionWarning = mode == AiTradeAdviceMode.ForcedDirectional
                ? " 积极模式会强制选择方向，不代表信号充分。"
                : string.Empty;
            SetWarning($"{advice.risk_warning} 预计交易后保证金占本金 {marginUsagePercent:0.0}%。{adjustedText}{forcedDirectionWarning} AI建议仅供参考，不构成投资建议。");
        }

        private void RenderSbiRuleState()
        {
            if (sbiRulesText == null)
            {
                return;
            }

            if (sbiRules == null || !sbiRules.IsUsable)
            {
                sbiRulesText.text = "SBI规则：尚未同步，请先读取官方最新规则。";
                return;
            }

            sbiRulesText.text =
                $"SBI规则：{sbiRules.Leverage}倍 / {sbiRules.MarginRatePercent:0.##}% / " +
                $"每1万通货 {sbiRules.RequiredMarginPer10000Jpy:N0} JPY / " +
                $"适用 {sbiRules.ApplicableDate} / 最小 {sbiRules.MinimumOrderUnits:N0} 通货";
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

        private static string LocalizeRelaySource(string sourceLabel)
        {
            switch (sourceLabel)
            {
                case "environment variable":
                    return "环境变量";
                case "Resources/AzureRelayConfig.json":
                    return "应用配置";
                default:
                    return sourceLabel;
            }
        }

        private void RenderRelayState(string sourceLabel)
        {
            marketSourceText.text = string.IsNullOrWhiteSpace(relayBaseUrl)
                ? "行情与AI：尚未配置 Azure 中转"
                : $"行情与AI：Azure后台中转（{LocalizeRelaySource(sourceLabel)}）";
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

        private double ParseInput(InputField input, double fallback)
        {
            if (input == null || !double.TryParse(input.text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                return fallback;
            }

            return value;
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
            }
        }

        private void SetWarning(string message)
        {
            if (warningsText != null)
            {
                warningsText.text = string.IsNullOrWhiteSpace(message) ? "AI建议仅供参考，不构成投资建议。" : message;
            }
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

            float safeWidthRatio = Mathf.Clamp(safeArea.width / screenSize.x, 0.01f, 1f);
            float safeHeightRatio = Mathf.Clamp(safeArea.height / screenSize.y, 0.01f, 1f);
            canvasScaler.referenceResolution = new Vector2(
                MobileReferenceResolution.x / safeWidthRatio,
                MobileReferenceResolution.y / safeHeightRatio);

            LayoutRebuilder.ForceRebuildLayoutImmediate(safeAreaContentRect);
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

        private Canvas CreateCanvas()
        {
            GameObject canvasObject = new GameObject("USDJPY Advisor Canvas");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvasObject.AddComponent<GraphicRaycaster>();

            canvasScaler = canvasObject.AddComponent<CanvasScaler>();
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

        private GameObject CreateUiObject(string name, Transform parent)
        {
            GameObject uiObject = new GameObject(name, typeof(RectTransform));
            uiObject.transform.SetParent(parent, false);
            RectTransform rect = uiObject.GetComponent<RectTransform>();
            rect.localScale = Vector3.one;
            return uiObject;
        }

        private Text AddHeader(Transform parent, string text)
        {
            Text label = AddText(parent, text, 22, FontStyle.Bold, new Color32(244, 247, 251, 255));
            LayoutElement layout = label.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 28;
            layout.preferredHeight = 28;
            return label;
        }

        private Text AddQuoteText(Transform parent, string text)
        {
            Text label = AddText(parent, text, 15, FontStyle.Bold, new Color32(244, 247, 251, 255));
            label.alignment = TextAnchor.UpperLeft;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 10;
            label.resizeTextMaxSize = 15;
            LayoutElement layout = label.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 30;
            layout.preferredHeight = 30;
            return label;
        }

        private Text AddValueRow(Transform parent, string label, string value)
        {
            GameObject field = CreateCompactField(parent, label);
            AddCompactFieldLabel(field.transform, label);

            GameObject valueObject = CreateUiObject(label + " Value", field.transform);
            Image valueBackground = valueObject.AddComponent<Image>();
            valueBackground.color = new Color32(14, 16, 19, 255);
            Text valueText = AddText(valueObject.transform, value, 12, FontStyle.Bold, new Color32(238, 242, 247, 255));
            valueText.alignment = TextAnchor.MiddleCenter;
            Stretch(valueText.GetComponent<RectTransform>());
            LayoutElement valueLayout = valueObject.AddComponent<LayoutElement>();
            valueLayout.minHeight = 32;
            valueLayout.preferredHeight = 32;
            return valueText;
        }

        private Text AddSectionTitle(Transform parent, string text)
        {
            Text label = AddText(parent, text, 13, FontStyle.Bold, new Color32(123, 217, 171, 255));
            LayoutElement layout = label.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 16;
            layout.preferredHeight = 16;
            return label;
        }

        private Text AddCompactInfoText(Transform parent, string text)
        {
            Text label = AddText(parent, text, 11, FontStyle.Normal, new Color32(170, 178, 190, 255));
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 8;
            label.resizeTextMaxSize = 11;
            LayoutElement layout = label.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 15;
            layout.preferredHeight = 15;
            return label;
        }

        private Text AddBodyText(Transform parent, string text)
        {
            Text label = AddText(parent, text, 12, FontStyle.Normal, new Color32(211, 218, 228, 255));
            label.alignment = TextAnchor.UpperLeft;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 8;
            label.resizeTextMaxSize = 12;
            LayoutElement layout = label.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 34;
            layout.preferredHeight = 34;
            layout.flexibleWidth = 1;
            return label;
        }

        private Transform CreateCompactSection(Transform parent, string title)
        {
            GameObject section = CreateUiObject(title + " Section", parent);
            LayoutElement sectionLayout = section.AddComponent<LayoutElement>();
            sectionLayout.minHeight = 63;
            sectionLayout.preferredHeight = 63;

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
            rowLayout.minHeight = 45;
            rowLayout.preferredHeight = 45;

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

        private Text AddCompactFieldLabel(Transform parent, string label)
        {
            Text labelText = AddText(parent, label, 10, FontStyle.Normal, new Color32(207, 214, 224, 255));
            labelText.verticalOverflow = VerticalWrapMode.Truncate;
            labelText.resizeTextForBestFit = true;
            labelText.resizeTextMinSize = 8;
            labelText.resizeTextMaxSize = 10;
            LayoutElement labelLayout = labelText.gameObject.AddComponent<LayoutElement>();
            labelLayout.minHeight = 12;
            labelLayout.preferredHeight = 12;
            return labelText;
        }

        private Text AddText(Transform parent, string value, int size, FontStyle style, Color32 color)
        {
            GameObject textObject = CreateUiObject("Text", parent);
            Text text = textObject.AddComponent<Text>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.lineSpacing = 1.05f;
            return text;
        }

        private InputField AddInput(Transform parent, string label, string defaultValue, bool password = false)
        {
            GameObject field = CreateCompactField(parent, label);
            AddCompactFieldLabel(field.transform, label);

            GameObject inputObject = CreateUiObject(label + " Input", field.transform);
            Image inputBackground = inputObject.AddComponent<Image>();
            inputBackground.color = new Color32(14, 16, 19, 255);
            Text inputText = CreateInputText(inputObject.transform, defaultValue);

            InputField input = inputObject.AddComponent<InputField>();
            input.targetGraphic = inputBackground;
            input.textComponent = inputText;
            input.placeholder = null;
            input.contentType = password ? InputField.ContentType.Password : InputField.ContentType.DecimalNumber;
            input.lineType = InputField.LineType.SingleLine;
            input.SetTextWithoutNotify(defaultValue);

            LayoutElement inputLayout = inputObject.AddComponent<LayoutElement>();
            inputLayout.flexibleWidth = 1;
            inputLayout.minHeight = 32;
            inputLayout.preferredHeight = 32;
            return input;
        }

        private Text CreateInputText(Transform parent, string value)
        {
            GameObject textObject = CreateUiObject("Input Text", parent);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(6, 2);
            rect.offsetMax = new Vector2(-6, -2);

            Text text = textObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = 12;
            text.color = new Color32(238, 242, 247, 255);
            text.text = value;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            return text;
        }

        private Dropdown AddDropdown(Transform parent, string label, List<string> options, int value)
        {
            GameObject field = CreateCompactField(parent, label);
            AddCompactFieldLabel(field.transform, label);

            GameObject dropdownObject = CreateUiObject(label + " Dropdown", field.transform);
            Image background = dropdownObject.AddComponent<Image>();
            background.color = new Color32(14, 16, 19, 255);
            Dropdown dropdown = dropdownObject.AddComponent<Dropdown>();
            dropdown.targetGraphic = background;
            dropdown.options = options.ConvertAll(option => new Dropdown.OptionData(option));
            dropdown.value = value;
            dropdown.captionText = AddText(dropdownObject.transform, options[value], 12, FontStyle.Normal, new Color32(238, 242, 247, 255));
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

        private void CreateDropdownTemplate(Dropdown dropdown, Transform parent)
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

            Text itemText = AddText(item.transform, "选项", 12, FontStyle.Normal, new Color32(238, 242, 247, 255));
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
            Text text = AddText(buttonObject.transform, string.IsNullOrWhiteSpace(buttonText) ? label : buttonText, 11, FontStyle.Bold, Color.white);
            text.alignment = TextAnchor.MiddleCenter;
            RectTransform textRect = text.GetComponent<RectTransform>();
            Stretch(textRect);

            LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
            layout.minHeight = 32;
            layout.preferredHeight = 32;
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
