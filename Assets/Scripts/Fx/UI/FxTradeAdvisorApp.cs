using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestFXTrade.Fx.Analysis;
using TestFXTrade.Fx.Domain;
using TestFXTrade.Fx.MarketData;
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
        private InputField equityInput;
        private Dropdown currencyDropdown;
        private InputField leverageInput;
        private InputField riskPercentInput;
        private InputField marginPercentInput;
        private InputField stopLossPipsInput;
        private InputField spreadPipsInput;
        private InputField longLotsInput;
        private InputField shortLotsInput;
        private InputField averageLongEntryInput;
        private InputField averageShortEntryInput;
        private Dropdown intervalDropdown;
        private Toggle autoRefreshToggle;
        private Text marketSourceText;
        private Text statusText;
        private Text quoteText;
        private Text metricsText;
        private Text recommendationText;
        private Text warningsText;
        private UsdJpyTrendLineGraphic chartGraphic;
        private CanvasScaler canvasScaler;
        private RectTransform safeAreaContentRect;
        private Rect lastSafeArea = new Rect(-1f, -1f, -1f, -1f);
        private Vector2Int lastScreenSize = new Vector2Int(-1, -1);

        private readonly RecommendationEngine recommendationEngine = new RecommendationEngine();
        private readonly List<Candle> latestCandles = new List<Candle>();
        private CancellationTokenSource marketDataCancellation;
        private IFxMarketDataProvider marketDataProvider;
        private MarketQuote latestQuote;
        private string apiKey = string.Empty;
        private float nextQuoteRefreshAt;
        private float nextCandleRefreshAt;
        private bool quoteRefreshInFlight;
        private bool candleRefreshInFlight;

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
            LocalFxSettings settings = LocalFxSettings.Load();
            apiKey = settings.ApiKey;

            if (settings.HasApiKey)
            {
                marketDataProvider = new TwelveDataMarketDataProvider(apiKey);
                marketSourceText.text = $"行情数据：Twelve Data（{LocalizeSourceLabel(settings.SourceLabel)}）";
                SetStatus("已读取本地 API Key，正在刷新 USD/JPY 实时行情。");
                nextQuoteRefreshAt = Time.time + QuoteRefreshSeconds;
                nextCandleRefreshAt = Time.time + CandleRefreshSeconds;
                _ = RefreshCandlesAndQuoteAsync(true);
                return;
            }

            marketSourceText.text = $"行情数据：缺少 {LocalFxSettings.ApiKeyVariableName}";
            SetStatus("请先在 .env 中配置 TWELVE_DATA_API_KEY。");
            warningsText.text = "请根据项目根目录中的 .env.example 创建 .env，然后重新启动。";
            nextQuoteRefreshAt = float.PositiveInfinity;
            nextCandleRefreshAt = float.PositiveInfinity;
        }

        private void Update()
        {
            RefreshAdaptiveLayout();

            if (string.IsNullOrWhiteSpace(apiKey) || !autoRefreshToggle.isOn)
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

            VerticalLayoutGroup contentGroup = content.AddComponent<VerticalLayoutGroup>();
            contentGroup.padding = new RectOffset(10, 10, 8, 8);
            contentGroup.spacing = 4;
            contentGroup.childControlWidth = true;
            contentGroup.childControlHeight = true;
            contentGroup.childForceExpandWidth = true;
            contentGroup.childForceExpandHeight = false;

            BuildMarketControls(content.transform);
            BuildOutputColumn(content.transform);
            BuildInputColumn(content.transform);
            RefreshAdaptiveLayout(true);
        }

        private void BuildMarketControls(Transform parent)
        {
            AddHeader(parent, "USD/JPY 交易助手");
            marketSourceText = AddCompactInfoText(parent, "正在读取本地行情配置……");

            Transform controls = CreateCompactFieldRow(parent, "Market Controls");
            AddValueRow(controls, "交易对", FxConstants.UsdJpySymbol);
            intervalDropdown = AddDropdown(controls, "周期", new List<string> { "1min", "5min", "15min" }, 1);
            intervalDropdown.onValueChanged.AddListener(unused => { _ = RefreshCandlesAndQuoteAsync(true); });
            autoRefreshToggle = AddToggle(controls, "实时 5秒", true);

            Button refreshButton = AddButton(controls, "手动刷新");
            refreshButton.onClick.AddListener(() => _ = RefreshCandlesAndQuoteAsync(true));

            statusText = AddCompactInfoText(parent, string.Empty);
            statusText.color = new Color32(190, 198, 210, 255);
        }

        private void BuildInputColumn(Transform parent)
        {
            Transform accountFields = CreateCompactSection(parent, "账户");
            principalInput = AddInput(accountFields, "本金", "1000000");
            equityInput = AddInput(accountFields, "净值", "1000000");
            currencyDropdown = AddDropdown(accountFields, "币种", new List<string> { "JPY", "USD" }, 0);
            leverageInput = AddInput(accountFields, "杠杆", "25");

            Transform riskFields = CreateCompactSection(parent, "风控参数");
            riskPercentInput = AddInput(riskFields, "单笔风险 %", "1");
            marginPercentInput = AddInput(riskFields, "保证金 %", "30");
            stopLossPipsInput = AddInput(riskFields, "止损 pips", "40");
            spreadPipsInput = AddInput(riskFields, "点差", "0.2");

            Transform positionFields = CreateCompactSection(parent, "当前持仓");
            longLotsInput = AddInput(positionFields, "多单 lots", "0");
            shortLotsInput = AddInput(positionFields, "空单 lots", "0");
            averageLongEntryInput = AddInput(positionFields, "多单均价", "0");
            averageShortEntryInput = AddInput(positionFields, "空单均价", "0");
        }

        private void BuildOutputColumn(Transform parent)
        {
            quoteText = AddQuoteText(parent, "正在等待 USD/JPY 实时行情");

            GameObject chartPanel = CreatePanel("ChartPanel", parent, new Color32(12, 14, 17, 255));
            LayoutElement chartLayout = chartPanel.AddComponent<LayoutElement>();
            chartLayout.minHeight = 94;
            chartLayout.preferredHeight = 94;
            chartLayout.flexibleWidth = 1;
            chartLayout.flexibleHeight = 1;
            GameObject chartLine = CreateUiObject("ChartLine", chartPanel.transform);
            chartLine.AddComponent<CanvasRenderer>();
            RectTransform chartLineRect = chartLine.GetComponent<RectTransform>();
            Stretch(chartLineRect);
            chartLineRect.offsetMin = new Vector2(12f, 12f);
            chartLineRect.offsetMax = new Vector2(-12f, -12f);
            chartGraphic = chartLine.AddComponent<UsdJpyTrendLineGraphic>();
            chartGraphic.color = Color.white;
            chartGraphic.raycastTarget = false;

            metricsText = AddBodyText(parent, "行情指标将在此显示。");
            recommendationText = AddBodyText(parent, "暂无交易建议。");
            LayoutElement recommendationLayout = recommendationText.GetComponent<LayoutElement>();
            recommendationLayout.minHeight = 42;
            recommendationLayout.preferredHeight = 42;
            recommendationText.fontSize = 12;
            recommendationText.color = new Color32(234, 239, 246, 255);
            warningsText = AddBodyText(parent, string.Empty);
            LayoutElement warningsLayout = warningsText.GetComponent<LayoutElement>();
            warningsLayout.minHeight = 28;
            warningsLayout.preferredHeight = 28;
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
            nextCandleRefreshAt = Time.time + CandleRefreshSeconds;
            nextQuoteRefreshAt = Time.time + QuoteRefreshSeconds;

            try
            {
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    SetStatus("缺少本地 API Key。");
                    warningsText.text = $"请先在 .env 中配置 {LocalFxSettings.ApiKeyVariableName}。";
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
                recommendationText.text = "观望：实时行情不可用，暂不提供交易建议。";
                warningsText.text = ex.Message;
            }
            finally
            {
                candleRefreshInFlight = false;
                quoteRefreshInFlight = false;
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
            nextQuoteRefreshAt = Time.time + QuoteRefreshSeconds;

            try
            {
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    SetStatus("缺少本地 API Key。");
                    warningsText.text = $"请先在 .env 中配置 {LocalFxSettings.ApiKeyVariableName}。";
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
                warningsText.text = ex.Message;
            }
            finally
            {
                quoteRefreshInFlight = false;
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
            TradeRecommendation recommendation = recommendationEngine.Build(
                ReadAccount(),
                ReadPosition(),
                ReadRiskProfile(),
                latestQuote,
                realtimeCandles);

            chartGraphic.SetCandles(realtimeCandles);
            RenderRecommendation(latestQuote, realtimeCandles, recommendation, interval);
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

        private AccountSnapshot ReadAccount()
        {
            AccountCurrency currency = currencyDropdown.value == 0 ? AccountCurrency.Jpy : AccountCurrency.Usd;
            return new AccountSnapshot(
                ParseInput(principalInput, 0d),
                ParseInput(equityInput, 0d),
                currency,
                ParseInput(leverageInput, 1d));
        }

        private PositionSnapshot ReadPosition()
        {
            return new PositionSnapshot(
                ParseInput(longLotsInput, 0d),
                ParseInput(shortLotsInput, 0d),
                ParseInput(averageLongEntryInput, 0d),
                ParseInput(averageShortEntryInput, 0d));
        }

        private RiskProfile ReadRiskProfile()
        {
            return new RiskProfile(
                ParseInput(riskPercentInput, 1d),
                ParseInput(marginPercentInput, 30d),
                ParseInput(stopLossPipsInput, 40d),
                ParseInput(spreadPipsInput, 0.2d));
        }

        private void RenderRecommendation(MarketQuote quote, IReadOnlyList<Candle> candles, TradeRecommendation recommendation, string interval)
        {
            string freshness = quote.IsTimestampReliable
                ? $"{quote.TimeUtc:yyyy-MM-dd HH:mm:ss} UTC"
                : "数据源时间戳不可用";

            quoteText.text = $"{quote.Symbol} {quote.Price:0.000}\n{freshness} | {interval} | {candles.Count} 根K线";

            AccountSnapshot account = ReadAccount();
            string currency = account.Currency == AccountCurrency.Jpy ? "JPY" : "USD";

            StringBuilder metrics = new StringBuilder();
            metrics.AppendLine($"趋势 {recommendation.TrendScore:0.00}   置信度 {recommendation.Confidence:0.00}   RSI {recommendation.Rsi:0.0}   ATR {recommendation.AtrPips:0.0} pips");
            metrics.AppendLine($"Pip/lot {recommendation.PipValuePerLotAccountCurrency:0.##} {currency}   保证金/lot {recommendation.MarginPerLotAccountCurrency:0.##} {currency}");
            metrics.Append($"当前净头寸 {recommendation.CurrentNetLots:0.00}   目标 {recommendation.TargetNetLots:0.00}   安全上限 {recommendation.MaxSafeGrossLots:0.00} lots");
            metricsText.text = metrics.ToString();

            StringBuilder advice = new StringBuilder();
            advice.AppendLine(recommendation.Summary);
            advice.Append($"买入 {recommendation.SuggestedBuyLots:0.00}   卖出 {recommendation.SuggestedSellLots:0.00}   保证金 {recommendation.RequiredMarginForSuggestion:0.##} {currency}");

            if (recommendation.Reasons.Count > 0)
            {
                advice.AppendLine();
                advice.Append(recommendation.Reasons[0]);
            }

            recommendationText.text = advice.ToString();

            if (recommendation.Warnings.Count == 0)
            {
                warningsText.text = "风险提示：当前参数没有触发阻断性警告。";
            }
            else
            {
                StringBuilder warnings = new StringBuilder("风险提示：");
                for (int i = 0; i < recommendation.Warnings.Count; i++)
                {
                    if (i > 0)
                    {
                        warnings.Append(" | ");
                    }

                    warnings.Append(recommendation.Warnings[i]);
                }

                warningsText.text = warnings.ToString();
            }
        }

        private CancellationToken GetMarketDataToken()
        {
            if (marketDataCancellation == null || marketDataCancellation.IsCancellationRequested)
            {
                marketDataCancellation = new CancellationTokenSource();
            }

            return marketDataCancellation.Token;
        }

        private IFxMarketDataProvider GetMarketDataProvider()
        {
            if (marketDataProvider == null)
            {
                marketDataProvider = new TwelveDataMarketDataProvider(apiKey);
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

        private static string LocalizeSourceLabel(string sourceLabel)
        {
            switch (sourceLabel)
            {
                case "environment variable":
                    return "环境变量";
                case "project .env":
                    return "项目 .env";
                case "current directory .env":
                    return "当前目录 .env";
                case "persistent .env":
                    return "持久化目录 .env";
                case "local .env":
                    return "本地 .env";
                default:
                    return sourceLabel;
            }
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

        private Button AddButton(Transform parent, string label)
        {
            GameObject field = CreateCompactField(parent, label);
            AddCompactFieldLabel(field.transform, label);

            GameObject buttonObject = CreateUiObject(label + " Button", field.transform);
            Image background = buttonObject.AddComponent<Image>();
            background.color = new Color32(55, 132, 93, 255);
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;
            Text text = AddText(buttonObject.transform, "刷新", 11, FontStyle.Bold, Color.white);
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
