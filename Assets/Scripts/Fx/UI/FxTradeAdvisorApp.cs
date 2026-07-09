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
        private const float AutoRefreshSeconds = 60f;

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
        private CancellationTokenSource refreshCancellation;
        private string apiKey = string.Empty;
        private float nextAutoRefreshAt;

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
                marketSourceText.text = $"Market data: Twelve Data ({settings.SourceLabel})";
                SetStatus("Local API key loaded. Refreshing live USD/JPY data.");
                nextAutoRefreshAt = Time.time + AutoRefreshSeconds;
                _ = RefreshAsync();
                return;
            }

            marketSourceText.text = $"Market data: missing {LocalFxSettings.ApiKeyVariableName}";
            SetStatus("Add TWELVE_DATA_API_KEY to .env before starting the app.");
            warningsText.text = "Create .env from .env.example in the project root, then restart.";
            nextAutoRefreshAt = float.PositiveInfinity;
        }

        private void Update()
        {
            if (string.IsNullOrWhiteSpace(apiKey) || !autoRefreshToggle.isOn || Time.time < nextAutoRefreshAt)
            {
                return;
            }

            nextAutoRefreshAt = Time.time + AutoRefreshSeconds;
            _ = RefreshAsync();
        }

        private void OnDestroy()
        {
            CancelRefresh();
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

            HorizontalLayoutGroup columns = root.AddComponent<HorizontalLayoutGroup>();
            columns.padding = new RectOffset(18, 18, 18, 18);
            columns.spacing = 16;
            columns.childControlHeight = true;
            columns.childControlWidth = true;
            columns.childForceExpandHeight = true;
            columns.childForceExpandWidth = false;

            GameObject left = CreatePanel("Inputs", root.transform, new Color32(31, 34, 38, 255));
            LayoutElement leftLayout = left.AddComponent<LayoutElement>();
            leftLayout.preferredWidth = 420;
            leftLayout.flexibleHeight = 1;
            VerticalLayoutGroup leftGroup = left.AddComponent<VerticalLayoutGroup>();
            leftGroup.padding = new RectOffset(18, 18, 18, 18);
            leftGroup.spacing = 10;
            leftGroup.childControlWidth = true;
            leftGroup.childControlHeight = true;
            leftGroup.childForceExpandWidth = true;
            leftGroup.childForceExpandHeight = false;

            GameObject right = CreatePanel("Analysis", root.transform, new Color32(24, 27, 31, 255));
            LayoutElement rightLayout = right.AddComponent<LayoutElement>();
            rightLayout.flexibleWidth = 1;
            rightLayout.flexibleHeight = 1;
            VerticalLayoutGroup rightGroup = right.AddComponent<VerticalLayoutGroup>();
            rightGroup.padding = new RectOffset(18, 18, 18, 18);
            rightGroup.spacing = 12;
            rightGroup.childControlWidth = true;
            rightGroup.childControlHeight = true;
            rightGroup.childForceExpandWidth = true;
            rightGroup.childForceExpandHeight = false;

            BuildInputColumn(left.transform);
            BuildOutputColumn(right.transform);
        }

        private void BuildInputColumn(Transform parent)
        {
            AddHeader(parent, "USD/JPY Advisor");
            marketSourceText = AddSmallText(parent, "Market data: loading local config.");

            AddDivider(parent);
            AddSectionTitle(parent, "Market");
            AddValueRow(parent, "Currency Pair", FxConstants.UsdJpySymbol);
            intervalDropdown = AddDropdown(parent, "Candle Interval", new List<string> { "1min", "5min", "15min" }, 1);
            autoRefreshToggle = AddToggle(parent, "Auto Refresh 60s", true);

            Button refreshButton = AddButton(parent, "Refresh Live Chart");
            refreshButton.onClick.AddListener(() => _ = RefreshAsync());

            statusText = AddSmallText(parent, string.Empty);
            statusText.color = new Color32(190, 198, 210, 255);

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
            chartLayout.minHeight = 260;
            chartLayout.flexibleWidth = 1;
            GameObject chartLine = CreateUiObject("ChartLine", chartPanel.transform);
            chartLine.AddComponent<CanvasRenderer>();
            Stretch(chartLine.GetComponent<RectTransform>());
            chartGraphic = chartLine.AddComponent<UsdJpyTrendLineGraphic>();
            chartGraphic.color = Color.white;
            chartGraphic.raycastTarget = false;

            metricsText = AddBodyText(parent, "Market metrics will appear here.");
            recommendationText = AddBodyText(parent, "No recommendation yet.");
            recommendationText.fontSize = 22;
            recommendationText.color = new Color32(234, 239, 246, 255);
            warningsText = AddBodyText(parent, string.Empty);
            warningsText.color = new Color32(244, 192, 102, 255);
        }

        private async Task RefreshAsync()
        {
            CancelRefresh();
            refreshCancellation = new CancellationTokenSource();
            CancellationToken token = refreshCancellation.Token;
            nextAutoRefreshAt = Time.time + AutoRefreshSeconds;

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

                IFxMarketDataProvider provider = new TwelveDataMarketDataProvider(apiKey);
                string interval = intervalDropdown.options[intervalDropdown.value].text;

                Task<MarketQuote> quoteTask = provider.GetLatestQuoteAsync(symbol, token);
                Task<IReadOnlyList<Candle>> candlesTask = provider.GetCandlesAsync(symbol, interval, 160, token);
                await Task.WhenAll(quoteTask, candlesTask);

                if (token.IsCancellationRequested)
                {
                    return;
                }

                MarketQuote quote = quoteTask.Result;
                IReadOnlyList<Candle> candles = candlesTask.Result;
                TradeRecommendation recommendation = recommendationEngine.Build(
                    ReadAccount(),
                    ReadPosition(),
                    ReadRiskProfile(),
                    quote,
                    candles);

                chartGraphic.SetCandles(candles);
                RenderRecommendation(quote, candles, recommendation, interval);
                SetStatus($"Updated from {provider.ProviderName} at {DateTime.Now:HH:mm:ss}.");
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

            quoteText.text = $"{quote.Symbol} {quote.Price:0.000}  |  {freshness}  |  {interval} x {candles.Count}";

            AccountSnapshot account = ReadAccount();
            string currency = account.Currency == AccountCurrency.Jpy ? "JPY" : "USD";

            StringBuilder metrics = new StringBuilder();
            metrics.AppendLine($"Trend Score: {recommendation.TrendScore:0.00}    Confidence: {recommendation.Confidence:0.00}");
            metrics.AppendLine($"RSI(14): {recommendation.Rsi:0.0}    ATR(14): {recommendation.AtrPips:0.0} pips");
            metrics.AppendLine($"Pip Value / lot: {recommendation.PipValuePerLotAccountCurrency:0.##} {currency}");
            metrics.AppendLine($"Margin / lot: {recommendation.MarginPerLotAccountCurrency:0.##} {currency}");
            metrics.AppendLine($"Current Net: {recommendation.CurrentNetLots:0.00} lots    Target Net: {recommendation.TargetNetLots:0.00} lots");
            metrics.AppendLine($"Max Safe Gross Lots: {recommendation.MaxSafeGrossLots:0.00}");
            metricsText.text = metrics.ToString();

            StringBuilder advice = new StringBuilder();
            advice.AppendLine(recommendation.Summary);
            advice.AppendLine($"Buy: {recommendation.SuggestedBuyLots:0.00} lots    Sell: {recommendation.SuggestedSellLots:0.00} lots");
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

        private void CancelRefresh()
        {
            if (refreshCancellation == null)
            {
                return;
            }

            refreshCancellation.Cancel();
            refreshCancellation.Dispose();
            refreshCancellation = null;
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

        private Canvas CreateCanvas()
        {
            GameObject canvasObject = new GameObject("USDJPY Advisor Canvas");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvasObject.AddComponent<GraphicRaycaster>();

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440, 900);
            scaler.matchWidthOrHeight = 0.5f;
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
            Text label = AddText(parent, text, 26, FontStyle.Bold, new Color32(244, 247, 251, 255));
            LayoutElement layout = label.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 34;
            return label;
        }

        private Text AddValueRow(Transform parent, string label, string value)
        {
            GameObject row = CreateUiObject(label + " Row", parent);
            LayoutElement rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.minHeight = 34;
            HorizontalLayoutGroup group = row.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 8;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = true;
            group.childForceExpandWidth = false;

            Text labelText = AddText(row.transform, label, 14, FontStyle.Normal, new Color32(207, 214, 224, 255));
            LayoutElement labelLayout = labelText.gameObject.AddComponent<LayoutElement>();
            labelLayout.preferredWidth = 170;

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
            layout.minHeight = 34;
            return label;
        }

        private Text AddBodyText(Transform parent, string text)
        {
            Text label = AddText(parent, text, 16, FontStyle.Normal, new Color32(211, 218, 228, 255));
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
            return text;
        }

        private InputField AddInput(Transform parent, string label, string defaultValue, bool password = false)
        {
            GameObject row = CreateUiObject(label + " Row", parent);
            LayoutElement rowLayout = row.AddComponent<LayoutElement>();
            rowLayout.minHeight = 42;
            HorizontalLayoutGroup group = row.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 8;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = true;
            group.childForceExpandWidth = false;

            Text labelText = AddText(row.transform, label, 14, FontStyle.Normal, new Color32(207, 214, 224, 255));
            LayoutElement labelLayout = labelText.gameObject.AddComponent<LayoutElement>();
            labelLayout.preferredWidth = 170;

            GameObject inputObject = CreateUiObject(label + " Input", row.transform);
            Image inputBackground = inputObject.AddComponent<Image>();
            inputBackground.color = new Color32(14, 16, 19, 255);
            Text inputText = CreateInputText(inputObject.transform, defaultValue);

            InputField input = inputObject.AddComponent<InputField>();
            input.targetGraphic = inputBackground;
            input.textComponent = inputText;
            input.placeholder = null;
            input.contentType = password ? InputField.ContentType.Password : InputField.ContentType.DecimalNumber;
            input.SetTextWithoutNotify(defaultValue);

            LayoutElement inputLayout = inputObject.AddComponent<LayoutElement>();
            inputLayout.flexibleWidth = 1;
            inputLayout.minHeight = 36;
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
            text.fontSize = 15;
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
            rowLayout.minHeight = 42;
            HorizontalLayoutGroup group = row.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 8;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = true;
            group.childForceExpandWidth = false;

            Text labelText = AddText(row.transform, label, 14, FontStyle.Normal, new Color32(207, 214, 224, 255));
            LayoutElement labelLayout = labelText.gameObject.AddComponent<LayoutElement>();
            labelLayout.preferredWidth = 170;

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
            dropdownLayout.minHeight = 36;
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
            templateRect.sizeDelta = new Vector2(0f, 140f);
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
            contentRect.sizeDelta = new Vector2(0f, 120f);
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
            rowLayout.minHeight = 36;
            HorizontalLayoutGroup group = row.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 10;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.childForceExpandHeight = true;
            group.childForceExpandWidth = false;

            GameObject box = CreateUiObject("Toggle Box", row.transform);
            Image boxImage = box.AddComponent<Image>();
            boxImage.color = new Color32(14, 16, 19, 255);
            LayoutElement boxLayout = box.AddComponent<LayoutElement>();
            boxLayout.preferredWidth = 28;
            boxLayout.preferredHeight = 28;

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
            layout.minHeight = 40;
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
