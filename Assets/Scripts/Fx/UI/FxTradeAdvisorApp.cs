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

            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
                marketSourceText.text = $"Market data: Twelve Data ({settings.SourceLabel})";
                SetStatus("Local API key loaded. Refreshing live USD/JPY data.");
                nextQuoteRefreshAt = Time.time + QuoteRefreshSeconds;
                nextCandleRefreshAt = Time.time + CandleRefreshSeconds;
                _ = RefreshCandlesAndQuoteAsync(true);
                return;
            }

            marketSourceText.text = $"Market data: missing {LocalFxSettings.ApiKeyVariableName}";
            SetStatus("Add TWELVE_DATA_API_KEY to .env before starting the app.");
            warningsText.text = "Create .env from .env.example in the project root, then restart.";
            nextQuoteRefreshAt = float.PositiveInfinity;
            nextCandleRefreshAt = float.PositiveInfinity;
        }

        private void Update()
        {
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

            ScrollRect scrollRect = root.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 28f;

            GameObject viewport = CreateUiObject("Viewport", root.transform);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            Stretch(viewportRect);
            ApplySafeArea(viewportRect);
            Image viewportImage = viewport.AddComponent<Image>();
            viewportImage.color = new Color32(18, 20, 23, 255);
            Mask viewportMask = viewport.AddComponent<Mask>();
            viewportMask.showMaskGraphic = false;

            GameObject content = CreateUiObject("Content", viewport.transform);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            VerticalLayoutGroup contentGroup = content.AddComponent<VerticalLayoutGroup>();
            contentGroup.padding = new RectOffset(16, 16, 18, 28);
            contentGroup.spacing = 12;
            contentGroup.childControlWidth = true;
            contentGroup.childControlHeight = true;
            contentGroup.childForceExpandWidth = true;
            contentGroup.childForceExpandHeight = false;

            ContentSizeFitter contentFitter = content.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;

            BuildMarketControls(content.transform);
            BuildOutputColumn(content.transform);
            BuildInputColumn(content.transform);
        }

        private void BuildMarketControls(Transform parent)
        {
            AddHeader(parent, "USD/JPY Advisor");
            marketSourceText = AddSmallText(parent, "Market data: loading local config.");

            AddDivider(parent);
            AddSectionTitle(parent, "Market");
            AddValueRow(parent, "Currency Pair", FxConstants.UsdJpySymbol);
            intervalDropdown = AddDropdown(parent, "Candle Interval", new List<string> { "1min", "5min", "15min" }, 1);
            intervalDropdown.onValueChanged.AddListener(unused => { _ = RefreshCandlesAndQuoteAsync(true); });
            autoRefreshToggle = AddToggle(parent, "Live Refresh 5s", true);

            Button refreshButton = AddButton(parent, "Refresh Live Chart");
            refreshButton.onClick.AddListener(() => _ = RefreshCandlesAndQuoteAsync(true));

            statusText = AddSmallText(parent, string.Empty);
            statusText.color = new Color32(190, 198, 210, 255);
        }

        private void BuildInputColumn(Transform parent)
        {
            AddDivider(parent);
            AddSectionTitle(parent, "Account");
            principalInput = AddInput(parent, "Principal", "1000000");
            equityInput = AddInput(parent, "Equity", "1000000");
            currencyDropdown = AddDropdown(parent, "Account Currency", new List<string> { "JPY", "USD" }, 0);
            leverageInput = AddInput(parent, "Leverage", "25");

            AddDivider(parent);
            AddSectionTitle(parent, "Risk Rules");
            riskPercentInput = AddInput(parent, "Risk Per Trade %", "1");
            marginPercentInput = AddInput(parent, "Max Margin Usage %", "30");
            stopLossPipsInput = AddInput(parent, "Planned Stop Pips", "40");
            spreadPipsInput = AddInput(parent, "Estimated Spread Pips", "0.2");

            AddDivider(parent);
            AddSectionTitle(parent, "Current Position");
            longLotsInput = AddInput(parent, "Current Long Lots", "0");
            shortLotsInput = AddInput(parent, "Current Short Lots", "0");
            averageLongEntryInput = AddInput(parent, "Avg Long Entry", "0");
            averageShortEntryInput = AddInput(parent, "Avg Short Entry", "0");
        }

        private void BuildOutputColumn(Transform parent)
        {
            quoteText = AddHeader(parent, "Waiting for live USD/JPY");

            GameObject chartPanel = CreatePanel("ChartPanel", parent, new Color32(12, 14, 17, 255));
            LayoutElement chartLayout = chartPanel.AddComponent<LayoutElement>();
            chartLayout.minHeight = 220;
            chartLayout.flexibleWidth = 1;
            GameObject chartLine = CreateUiObject("ChartLine", chartPanel.transform);
            chartLine.AddComponent<CanvasRenderer>();
            RectTransform chartLineRect = chartLine.GetComponent<RectTransform>();
            Stretch(chartLineRect);
            chartLineRect.offsetMin = new Vector2(12f, 12f);
            chartLineRect.offsetMax = new Vector2(-12f, -12f);
            chartGraphic = chartLine.AddComponent<UsdJpyTrendLineGraphic>();
            chartGraphic.color = Color.white;
            chartGraphic.raycastTarget = false;

            metricsText = AddBodyText(parent, "Market metrics will appear here.");
            recommendationText = AddBodyText(parent, "No recommendation yet.");
            recommendationText.fontSize = 18;
            recommendationText.color = new Color32(234, 239, 246, 255);
            warningsText = AddBodyText(parent, string.Empty);
            warningsText.color = new Color32(244, 192, 102, 255);
        }

        private async Task RefreshCandlesAndQuoteAsync(bool manual)
        {
            if (quoteRefreshInFlight || candleRefreshInFlight)
            {
                if (manual)
                {
                    SetStatus("Live refresh is already running.");
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
                    SetStatus("Missing local API key.");
                    warningsText.text = $"Set {LocalFxSettings.ApiKeyVariableName} in .env before starting the app.";
                    return;
                }

                string symbol = FxConstants.UsdJpySymbol;
                SetStatus($"Fetching live {symbol} quote and candles...");

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
                RenderLatestMarketState(interval, $"Updated from {provider.ProviderName} at {DateTime.Now:HH:mm:ss}.");
            }
            catch (OperationCanceledException)
            {
                SetStatus("Refresh cancelled.");
            }
            catch (Exception ex)
            {
                SetStatus("Live data unavailable.");
                recommendationText.text = "Hold: no recommendation while live data is unavailable.";
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
                    SetStatus("Live quote refresh is already running.");
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
                    SetStatus("Missing local API key.");
                    warningsText.text = $"Set {LocalFxSettings.ApiKeyVariableName} in .env before starting the app.";
                    return;
                }

                string symbol = FxConstants.UsdJpySymbol;
                SetStatus($"Updating live {symbol} quote...");

                IFxMarketDataProvider provider = GetMarketDataProvider();
                latestQuote = await provider.GetLatestQuoteAsync(symbol, token);

                if (token.IsCancellationRequested)
                {
                    return;
                }

                RenderLatestMarketState(GetSelectedInterval(), $"Live quote updated at {DateTime.Now:HH:mm:ss}.");
            }
            catch (OperationCanceledException)
            {
                SetStatus("Refresh cancelled.");
            }
            catch (Exception ex)
            {
                SetStatus("Live quote unavailable.");
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
                : "provider timestamp unavailable";

            quoteText.text = $"{quote.Symbol} {quote.Price:0.000}\n{freshness}  |  {interval} x {candles.Count}";

            AccountSnapshot account = ReadAccount();
            string currency = account.Currency == AccountCurrency.Jpy ? "JPY" : "USD";

            StringBuilder metrics = new StringBuilder();
            metrics.AppendLine($"Trend Score: {recommendation.TrendScore:0.00}");
            metrics.AppendLine($"Confidence: {recommendation.Confidence:0.00}");
            metrics.AppendLine($"RSI(14): {recommendation.Rsi:0.0}");
            metrics.AppendLine($"ATR(14): {recommendation.AtrPips:0.0} pips");
            metrics.AppendLine($"Pip Value / lot: {recommendation.PipValuePerLotAccountCurrency:0.##} {currency}");
            metrics.AppendLine($"Margin / lot: {recommendation.MarginPerLotAccountCurrency:0.##} {currency}");
            metrics.AppendLine($"Current Net: {recommendation.CurrentNetLots:0.00} lots");
            metrics.AppendLine($"Target Net: {recommendation.TargetNetLots:0.00} lots");
            metrics.AppendLine($"Max Safe Gross Lots: {recommendation.MaxSafeGrossLots:0.00}");
            metricsText.text = metrics.ToString();

            StringBuilder advice = new StringBuilder();
            advice.AppendLine(recommendation.Summary);
            advice.AppendLine($"Buy: {recommendation.SuggestedBuyLots:0.00} lots");
            advice.AppendLine($"Sell: {recommendation.SuggestedSellLots:0.00} lots");
            advice.AppendLine($"Estimated margin for suggestion: {recommendation.RequiredMarginForSuggestion:0.##} {currency}");

            for (int i = 0; i < recommendation.Reasons.Count; i++)
            {
                advice.AppendLine("- " + recommendation.Reasons[i]);
            }

            recommendationText.text = advice.ToString();

            if (recommendation.Warnings.Count == 0)
            {
                warningsText.text = "Risk notes: no blocking warning from the current inputs.";
            }
            else
            {
                StringBuilder warnings = new StringBuilder("Risk notes:\n");
                for (int i = 0; i < recommendation.Warnings.Count; i++)
                {
                    warnings.AppendLine("- " + recommendation.Warnings[i]);
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

        private static void ApplySafeArea(RectTransform rect)
        {
            if (Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }

            Rect safeArea = Screen.safeArea;
            Vector2 anchorMin = safeArea.position;
            Vector2 anchorMax = safeArea.position + safeArea.size;
            anchorMin.x /= Screen.width;
            anchorMin.y /= Screen.height;
            anchorMax.x /= Screen.width;
            anchorMax.y /= Screen.height;

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

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.referenceResolution = MobileReferenceResolution;
            scaler.matchWidthOrHeight = 0f;
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
            Text label = AddText(parent, text, 24, FontStyle.Bold, new Color32(244, 247, 251, 255));
            LayoutElement layout = label.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 38;
            return label;
        }

        private Text AddValueRow(Transform parent, string label, string value)
        {
            GameObject row = CreateUiObject(label + " Row", parent);
            LayoutElement rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.minHeight = 42;
            HorizontalLayoutGroup group = row.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 10;
            group.padding = new RectOffset(0, 0, 4, 4);
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = false;
            group.childForceExpandWidth = false;

            Text labelText = AddText(row.transform, label, 13, FontStyle.Normal, new Color32(207, 214, 224, 255));
            LayoutElement labelLayout = labelText.gameObject.AddComponent<LayoutElement>();
            labelLayout.preferredWidth = 136;

            Text valueText = AddText(row.transform, value, 15, FontStyle.Bold, new Color32(238, 242, 247, 255));
            LayoutElement valueLayout = valueText.gameObject.AddComponent<LayoutElement>();
            valueLayout.flexibleWidth = 1;
            return valueText;
        }

        private Text AddSectionTitle(Transform parent, string text)
        {
            Text label = AddText(parent, text, 17, FontStyle.Bold, new Color32(123, 217, 171, 255));
            LayoutElement layout = label.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 26;
            return label;
        }

        private Text AddSmallText(Transform parent, string text)
        {
            Text label = AddText(parent, text, 13, FontStyle.Normal, new Color32(170, 178, 190, 255));
            LayoutElement layout = label.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 30;
            return label;
        }

        private Text AddBodyText(Transform parent, string text)
        {
            Text label = AddText(parent, text, 16, FontStyle.Normal, new Color32(211, 218, 228, 255));
            label.alignment = TextAnchor.UpperLeft;
            LayoutElement layout = label.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 82;
            layout.flexibleWidth = 1;
            return label;
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
            GameObject row = CreateUiObject(label + " Row", parent);
            LayoutElement rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.minHeight = 74;
            VerticalLayoutGroup group = row.AddComponent<VerticalLayoutGroup>();
            group.spacing = 6;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = false;
            group.childForceExpandWidth = true;

            Text labelText = AddText(row.transform, label, 13, FontStyle.Normal, new Color32(207, 214, 224, 255));
            LayoutElement labelLayout = labelText.gameObject.AddComponent<LayoutElement>();
            labelLayout.minHeight = 18;

            GameObject inputObject = CreateUiObject(label + " Input", row.transform);
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
            inputLayout.minHeight = 44;
            inputLayout.preferredHeight = 44;
            return input;
        }

        private Text CreateInputText(Transform parent, string value)
        {
            GameObject textObject = CreateUiObject("Input Text", parent);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(10, 5);
            rect.offsetMax = new Vector2(-10, -5);

            Text text = textObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = 16;
            text.color = new Color32(238, 242, 247, 255);
            text.text = value;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            return text;
        }

        private Dropdown AddDropdown(Transform parent, string label, List<string> options, int value)
        {
            GameObject row = CreateUiObject(label + " Row", parent);
            LayoutElement rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.minHeight = 74;
            VerticalLayoutGroup group = row.AddComponent<VerticalLayoutGroup>();
            group.spacing = 6;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = false;
            group.childForceExpandWidth = true;

            Text labelText = AddText(row.transform, label, 13, FontStyle.Normal, new Color32(207, 214, 224, 255));
            LayoutElement labelLayout = labelText.gameObject.AddComponent<LayoutElement>();
            labelLayout.minHeight = 18;

            GameObject dropdownObject = CreateUiObject(label + " Dropdown", row.transform);
            Image background = dropdownObject.AddComponent<Image>();
            background.color = new Color32(14, 16, 19, 255);
            Dropdown dropdown = dropdownObject.AddComponent<Dropdown>();
            dropdown.targetGraphic = background;
            dropdown.options = options.ConvertAll(option => new Dropdown.OptionData(option));
            dropdown.value = value;
            dropdown.captionText = AddText(dropdownObject.transform, options[value], 15, FontStyle.Normal, new Color32(238, 242, 247, 255));
            RectTransform captionRect = dropdown.captionText.GetComponent<RectTransform>();
            captionRect.anchorMin = Vector2.zero;
            captionRect.anchorMax = Vector2.one;
            captionRect.offsetMin = new Vector2(10, 5);
            captionRect.offsetMax = new Vector2(-10, -5);
            CreateDropdownTemplate(dropdown, dropdownObject.transform);

            LayoutElement dropdownLayout = dropdownObject.AddComponent<LayoutElement>();
            dropdownLayout.flexibleWidth = 1;
            dropdownLayout.minHeight = 44;
            dropdownLayout.preferredHeight = 44;
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
            templateRect.sizeDelta = new Vector2(0f, 156f);
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
            contentRect.sizeDelta = new Vector2(0f, 132f);
            VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
            contentLayout.childControlHeight = true;
            contentLayout.childControlWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.childForceExpandWidth = true;
            ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject item = CreateUiObject("Item", content.transform);
            RectTransform itemRect = item.GetComponent<RectTransform>();
            itemRect.sizeDelta = new Vector2(0f, 40f);
            LayoutElement itemLayout = item.AddComponent<LayoutElement>();
            itemLayout.minHeight = 40f;
            itemLayout.preferredHeight = 40f;
            Image itemBackground = item.AddComponent<Image>();
            itemBackground.color = new Color32(24, 28, 32, 255);
            Toggle itemToggle = item.AddComponent<Toggle>();
            itemToggle.targetGraphic = itemBackground;

            Text itemText = AddText(item.transform, "Option", 14, FontStyle.Normal, new Color32(238, 242, 247, 255));
            RectTransform itemTextRect = itemText.GetComponent<RectTransform>();
            itemTextRect.anchorMin = Vector2.zero;
            itemTextRect.anchorMax = Vector2.one;
            itemTextRect.offsetMin = new Vector2(10f, 0f);
            itemTextRect.offsetMax = new Vector2(-10f, 0f);

            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            dropdown.template = templateRect;
            dropdown.itemText = itemText;
        }

        private Toggle AddToggle(Transform parent, string label, bool defaultValue)
        {
            GameObject row = CreateUiObject(label + " Row", parent);
            LayoutElement rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.minHeight = 44;
            HorizontalLayoutGroup group = row.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 10;
            group.padding = new RectOffset(0, 0, 7, 7);
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = false;
            group.childForceExpandWidth = false;

            GameObject box = CreateUiObject("Toggle Box", row.transform);
            Image boxImage = box.AddComponent<Image>();
            boxImage.color = new Color32(14, 16, 19, 255);
            LayoutElement boxLayout = box.AddComponent<LayoutElement>();
            boxLayout.preferredWidth = 30;
            boxLayout.preferredHeight = 30;

            GameObject checkmark = CreateUiObject("Checkmark", box.transform);
            Image checkmarkImage = checkmark.AddComponent<Image>();
            checkmarkImage.color = new Color32(123, 217, 171, 255);
            RectTransform checkmarkRect = checkmark.GetComponent<RectTransform>();
            checkmarkRect.anchorMin = new Vector2(0.25f, 0.25f);
            checkmarkRect.anchorMax = new Vector2(0.75f, 0.75f);
            checkmarkRect.offsetMin = Vector2.zero;
            checkmarkRect.offsetMax = Vector2.zero;

            Toggle toggle = row.AddComponent<Toggle>();
            toggle.targetGraphic = boxImage;
            toggle.graphic = checkmarkImage;
            toggle.isOn = defaultValue;

            Text text = AddText(row.transform, label, 14, FontStyle.Normal, new Color32(207, 214, 224, 255));
            LayoutElement textLayout = text.gameObject.AddComponent<LayoutElement>();
            textLayout.flexibleWidth = 1;
            textLayout.minHeight = 30;
            return toggle;
        }

        private Button AddButton(Transform parent, string label)
        {
            GameObject buttonObject = CreateUiObject(label + " Button", parent);
            Image background = buttonObject.AddComponent<Image>();
            background.color = new Color32(55, 132, 93, 255);
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;
            Text text = AddText(buttonObject.transform, label, 15, FontStyle.Bold, Color.white);
            text.alignment = TextAnchor.MiddleCenter;
            RectTransform textRect = text.GetComponent<RectTransform>();
            Stretch(textRect);

            LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
            layout.minHeight = 48;
            layout.preferredHeight = 48;
            return button;
        }

        private void AddDivider(Transform parent)
        {
            GameObject divider = CreateUiObject("Divider", parent);
            Image image = divider.AddComponent<Image>();
            image.color = new Color32(255, 255, 255, 28);
            LayoutElement layout = divider.AddComponent<LayoutElement>();
            layout.minHeight = 1;
            layout.preferredHeight = 1;
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
