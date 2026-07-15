const { app } = require("@azure/functions");
const {
  adviceHandler,
  candlesHandler,
  healthHandler,
  quoteHandler,
  withErrorHandling,
} = require("./relayService");

app.http("health", {
  methods: ["GET"],
  authLevel: "anonymous",
  route: "health",
  handler: withErrorHandling(healthHandler),
});

app.http("marketQuote", {
  methods: ["GET"],
  authLevel: "anonymous",
  route: "market/quote",
  handler: withErrorHandling(quoteHandler),
});

app.http("marketCandles", {
  methods: ["GET"],
  authLevel: "anonymous",
  route: "market/candles",
  handler: withErrorHandling(candlesHandler),
});

app.http("tradeAdvice", {
  methods: ["POST"],
  authLevel: "anonymous",
  route: "advice",
  handler: withErrorHandling(adviceHandler),
});
