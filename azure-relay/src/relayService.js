const TWELVE_DATA_BASE_URL = "https://api.twelvedata.com";
const OPENAI_RESPONSES_URL = "https://api.openai.com/v1/responses";
const DEFAULT_OPENAI_MODEL = "gpt-5.6";
const ALLOWED_SYMBOL = "USD/JPY";
const ALLOWED_INTERVALS = new Set(["1min", "5min", "15min"]);
const ALLOWED_RESPONSE_LANGUAGES = new Set(["Simplified Chinese", "English", "Japanese"]);
const MAX_PROMPT_LENGTH = 24000;

const COMMON_INSTRUCTIONS_PREFIX =
  "You are a USD/JPY decision-support assistant. Return only the requested JSON schema. " +
  "Treat the SBI FX rule snapshot as a hard margin constraint, " +
  "not as a guarantee that a trade is safe. The JSON field suggested_lots remains the internal next-order size in " +
  "standard lots, where 1 standard lot equals 100,000 base-currency units. " +
  "Use current_position_entry_price only when provided; never invent missing facts or prices. Respect the mode-specific margin limit. " +
  "Explicitly mention uncertainty and that the result is informational, not personalized investment advice. " +
  "Keep summary, reasoning, and risk_warning concise.";

class RelayError extends Error {
  constructor(status, publicMessage, logMessage = publicMessage) {
    super(logMessage);
    this.name = "RelayError";
    this.status = status;
    this.publicMessage = publicMessage;
  }
}

function json(status, jsonBody) {
  return {
    status,
    jsonBody,
    headers: {
      "Cache-Control": "no-store",
      "Content-Type": "application/json; charset=utf-8",
    },
  };
}

function withErrorHandling(handler) {
  return async (request, context) => {
    try {
      return await handler(request, context);
    } catch (error) {
      const relayError = error instanceof RelayError
        ? error
        : new RelayError(500, "后台中转发生未预期错误。", error?.message || String(error));
      context?.error?.(relayError.message);
      return json(relayError.status, { error: relayError.publicMessage });
    }
  };
}

async function healthHandler() {
  return json(200, {
    status: "ok",
    marketConfigured: Boolean(process.env.TWELVE_DATA_API_KEY),
    openAiConfigured: Boolean(process.env.OPENAI_API_KEY),
  });
}

async function quoteHandler(request) {
  const symbol = readSymbol(request.query.get("symbol"));
  const apiKey = requireSetting("TWELVE_DATA_API_KEY");
  const url = new URL(`${TWELVE_DATA_BASE_URL}/quote`);
  url.searchParams.set("symbol", symbol);
  url.searchParams.set("apikey", apiKey);

  const upstream = await fetchJson(url, {}, "Twelve Data");
  throwIfTwelveDataError(upstream);

  const price = readPositiveNumber(upstream.close, "报价");
  const timestamp = Number(upstream.timestamp);
  const hasTimestamp = Number.isFinite(timestamp) && timestamp > 0;
  return json(200, {
    symbol,
    price,
    timeUtc: hasTimestamp ? new Date(timestamp * 1000).toISOString() : new Date().toISOString(),
    isTimestampReliable: hasTimestamp,
    provider: "Twelve Data（Azure中转）",
  });
}

async function candlesHandler(request) {
  const symbol = readSymbol(request.query.get("symbol"));
  const interval = readInterval(request.query.get("interval"));
  const outputSize = readOutputSize(request.query.get("outputSize"));
  const apiKey = requireSetting("TWELVE_DATA_API_KEY");
  const url = new URL(`${TWELVE_DATA_BASE_URL}/time_series`);
  url.searchParams.set("symbol", symbol);
  url.searchParams.set("interval", interval);
  url.searchParams.set("outputsize", String(outputSize));
  url.searchParams.set("timezone", "UTC");
  url.searchParams.set("apikey", apiKey);

  const upstream = await fetchJson(url, {}, "Twelve Data");
  throwIfTwelveDataError(upstream);
  if (!Array.isArray(upstream.values) || upstream.values.length === 0) {
    throw new RelayError(502, "行情供应商未返回K线数据。", "Twelve Data returned no candle values");
  }

  const candles = upstream.values.map((row) => ({
    timeUtc: parseUtcDate(row.datetime),
    open: readPositiveNumber(row.open, "K线开盘价"),
    high: readPositiveNumber(row.high, "K线最高价"),
    low: readPositiveNumber(row.low, "K线最低价"),
    close: readPositiveNumber(row.close, "K线收盘价"),
  }));
  candles.sort((left, right) => Date.parse(left.timeUtc) - Date.parse(right.timeUtc));

  return json(200, { candles });
}

async function adviceHandler(request) {
  const apiKey = requireSetting("OPENAI_API_KEY");
  const payload = await readRequestJson(request);
  const prompt = typeof payload.prompt === "string" ? payload.prompt.trim() : "";
  const mode = payload.mode === "forced_directional" ? "forced_directional" :
    payload.mode === "conservative" ? "conservative" : "";
  const requestedLanguage = typeof payload.language === "string"
    ? payload.language
    : "Simplified Chinese";
  const language = ALLOWED_RESPONSE_LANGUAGES.has(requestedLanguage)
    ? requestedLanguage
    : "";

  if (prompt.length === 0 || prompt.length > MAX_PROMPT_LENGTH) {
    throw new RelayError(400, `AI提示词长度必须在1到${MAX_PROMPT_LENGTH}字符之间。`);
  }

  if (!mode) {
    throw new RelayError(400, "AI策略模式无效。");
  }

  if (!language) {
    throw new RelayError(400, "AI响应语言无效。");
  }

  const openAiRequest = buildOpenAiRequest(
    prompt,
    mode,
    process.env.OPENAI_MODEL || DEFAULT_OPENAI_MODEL,
    language,
  );
  const upstream = await fetchJson(
    OPENAI_RESPONSES_URL,
    {
      method: "POST",
      headers: {
        Authorization: `Bearer ${apiKey}`,
        "Content-Type": "application/json",
      },
      body: JSON.stringify(openAiRequest),
    },
    "OpenAI",
    70000,
  );

  const advice = parseOpenAiAdvice(upstream, mode);
  return json(200, advice);
}

function buildOpenAiRequest(
  prompt,
  mode,
  model = DEFAULT_OPENAI_MODEL,
  language = "Simplified Chinese",
) {
  return {
    model,
    instructions: getInstructions(mode, language),
    input: prompt,
    store: false,
    max_output_tokens: 700,
    text: {
      format: {
        type: "json_schema",
        name: "fx_trade_advice",
        strict: true,
        schema: buildAdviceSchema(mode),
      },
    },
  };
}

function getInstructions(mode, language = "Simplified Chinese") {
  const commonInstructions = COMMON_INSTRUCTIONS_PREFIX + getLanguageInstructions(language);
  if (mode === "forced_directional") {
    return commonInstructions +
      " This is a relatively aggressive forced-direction scenario. You must choose BUY or SELL and never HOLD. " +
      "When evidence is weak or conflicting, choose the more defensible direction, lower confidence, and use a smaller feasible order. " +
      "Do not use forced direction as a reason to ignore uncertainty, the SBI minimum order, or the 70% margin limit.";
  }

  return commonInstructions +
    " This is the conservative scenario. Choose BUY, SELL, or HOLD; HOLD must use suggested_lots=0. " +
    "Prefer HOLD when data is stale, insufficient, conflicting, or when the current position is already aggressive. " +
    "Keep estimated post-trade required margin at or below 50% of principal unless the order only reduces exposure.";
}

function getLanguageInstructions(language) {
  switch (language) {
    case "English":
      return " Answer all natural-language fields in English. In summary, reasoning, and risk_warning, " +
        "describe size as position size in base-currency units, not as lot or lots.";
    case "Japanese":
      return " Answer all natural-language fields in Japanese. In summary, reasoning, and risk_warning, " +
        "describe size as 建玉数量 in base-currency units (通貨), not as lot, lots, or ロット.";
    default:
      return " Answer all natural-language fields in Simplified Chinese. In summary, reasoning, and risk_warning, " +
        "describe size as 建玉数量 in base-currency units (通貨), not as lot or 手数.";
  }
}

function buildAdviceSchema(mode) {
  return {
    type: "object",
    properties: {
      action: {
        type: "string",
        enum: mode === "forced_directional" ? ["BUY", "SELL"] : ["BUY", "SELL", "HOLD"],
      },
      suggested_lots: { type: "number" },
      confidence: { type: "number" },
      summary: { type: "string" },
      reasoning: { type: "string" },
      risk_warning: { type: "string" },
    },
    required: ["action", "suggested_lots", "confidence", "summary", "reasoning", "risk_warning"],
    additionalProperties: false,
  };
}

function parseOpenAiAdvice(response, mode) {
  if (response?.error?.message) {
    throw new RelayError(502, "OpenAI暂时无法生成建议。", `OpenAI error: ${response.error.message}`);
  }

  let outputText = "";
  for (const output of response?.output || []) {
    for (const content of output?.content || []) {
      if (content?.refusal) {
        throw new RelayError(502, "OpenAI拒绝生成本次建议。", `OpenAI refusal: ${content.refusal}`);
      }

      if (content?.type === "output_text" && typeof content.text === "string") {
        outputText = content.text;
        break;
      }
    }
  }

  if (!outputText) {
    throw new RelayError(502, "OpenAI未返回可用的结构化建议。", "OpenAI response had no output_text");
  }

  let advice;
  try {
    advice = JSON.parse(outputText);
  } catch (error) {
    throw new RelayError(502, "OpenAI返回的建议格式无效。", `Invalid OpenAI output JSON: ${error.message}`);
  }

  const action = typeof advice.action === "string" ? advice.action.trim().toUpperCase() : "";
  const validActions = mode === "forced_directional" ? ["BUY", "SELL"] : ["BUY", "SELL", "HOLD"];
  if (!validActions.includes(action)) {
    throw new RelayError(502, "OpenAI返回了无效的交易方向。", `Invalid action for ${mode}: ${action}`);
  }

  const suggestedLots = Number(advice.suggested_lots);
  const confidence = Number(advice.confidence);
  for (const field of ["summary", "reasoning", "risk_warning"]) {
    if (typeof advice[field] !== "string") {
      throw new RelayError(502, "OpenAI返回的建议字段不完整。", `Invalid advice field: ${field}`);
    }
  }

  if (!Number.isFinite(suggestedLots) || !Number.isFinite(confidence)) {
    throw new RelayError(502, "OpenAI返回的建议数值无效。", "Invalid numeric advice field");
  }

  return {
    action,
    suggested_lots: action === "HOLD" ? 0 : Math.max(0, suggestedLots),
    confidence: Math.max(0, Math.min(1, confidence)),
    summary: advice.summary,
    reasoning: advice.reasoning,
    risk_warning: advice.risk_warning,
  };
}

async function fetchJson(url, options, providerName, timeoutMs = 25000) {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), timeoutMs);
  let response;

  try {
    response = await fetch(url, { ...options, signal: controller.signal });
  } catch (error) {
    throw new RelayError(502, `${providerName}暂时不可用。`, `${providerName} network error: ${error.message}`);
  } finally {
    clearTimeout(timeout);
  }

  let body;
  try {
    body = await response.json();
  } catch (error) {
    throw new RelayError(502, `${providerName}返回了无效响应。`, `${providerName} JSON error: ${error.message}`);
  }

  if (!response.ok) {
    const upstreamMessage = body?.error?.message || body?.message || `HTTP ${response.status}`;
    throw new RelayError(502, `${providerName}请求失败。`, `${providerName} upstream error: ${upstreamMessage}`);
  }

  return body;
}

async function readRequestJson(request) {
  try {
    return await request.json();
  } catch (error) {
    throw new RelayError(400, "请求正文不是有效JSON。", error.message);
  }
}

function readSymbol(value) {
  const symbol = (value || ALLOWED_SYMBOL).trim().toUpperCase();
  if (symbol !== ALLOWED_SYMBOL) {
    throw new RelayError(400, "目前只支持 USD/JPY。");
  }

  return symbol;
}

function readInterval(value) {
  const interval = (value || "5min").trim();
  if (!ALLOWED_INTERVALS.has(interval)) {
    throw new RelayError(400, "K线周期只支持 1min、5min 或 15min。");
  }

  return interval;
}

function readOutputSize(value) {
  const parsed = Number.parseInt(value || "160", 10);
  if (!Number.isFinite(parsed)) {
    throw new RelayError(400, "K线数量无效。");
  }

  return Math.max(30, Math.min(500, parsed));
}

function readPositiveNumber(value, label) {
  const parsed = Number(value);
  if (!Number.isFinite(parsed) || parsed <= 0) {
    throw new RelayError(502, "行情供应商返回了无效数据。", `Invalid ${label}: ${value}`);
  }

  return parsed;
}

function parseUtcDate(value) {
  const normalized = typeof value === "string" ? value.trim().replace(" ", "T") : "";
  const withZone = /(?:Z|[+-]\d{2}:?\d{2})$/i.test(normalized) ? normalized : `${normalized}Z`;
  const timestamp = Date.parse(withZone);
  if (!Number.isFinite(timestamp)) {
    throw new RelayError(502, "行情供应商返回了无效时间。", `Invalid candle time: ${value}`);
  }

  return new Date(timestamp).toISOString();
}

function throwIfTwelveDataError(response) {
  if (response?.status === "error") {
    throw new RelayError(502, "行情供应商请求失败。", `Twelve Data error: ${response.message || "unknown"}`);
  }
}

function requireSetting(name) {
  const value = process.env[name];
  if (!value) {
    throw new RelayError(503, "后台供应商配置尚未完成。", `Missing Function App setting: ${name}`);
  }

  return value;
}

module.exports = {
  adviceHandler,
  buildAdviceSchema,
  buildOpenAiRequest,
  candlesHandler,
  healthHandler,
  parseOpenAiAdvice,
  quoteHandler,
  withErrorHandling,
};
