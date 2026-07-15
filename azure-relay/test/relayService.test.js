const assert = require("node:assert/strict");
const test = require("node:test");
const {
  buildAdviceSchema,
  buildOpenAiRequest,
  parseOpenAiAdvice,
} = require("../src/relayService");

test("conservative schema allows HOLD", () => {
  const schema = buildAdviceSchema("conservative");
  assert.deepEqual(schema.properties.action.enum, ["BUY", "SELL", "HOLD"]);
});

test("forced directional schema excludes HOLD", () => {
  const request = buildOpenAiRequest("prompt", "forced_directional", "test-model");
  assert.equal(request.model, "test-model");
  assert.equal(request.store, false);
  assert.deepEqual(request.text.format.schema.properties.action.enum, ["BUY", "SELL"]);
  assert.match(request.instructions, /never HOLD/);
  assert.match(request.instructions, /建玉数量/);
  assert.match(request.instructions, /not as lot/);
});

test("OpenAI structured output is normalized for the client", () => {
  const response = {
    output: [{
      content: [{
        type: "output_text",
        text: JSON.stringify({
          action: "buy",
          suggested_lots: 0.02,
          confidence: 1.2,
          summary: "短线偏多",
          reasoning: "趋势与动量一致",
          risk_warning: "注意波动",
        }),
      }],
    }],
  };

  const advice = parseOpenAiAdvice(response, "conservative");
  assert.equal(advice.action, "BUY");
  assert.equal(advice.suggested_lots, 0.02);
  assert.equal(advice.confidence, 1);
});

test("forced directional mode rejects HOLD even if upstream returns it", () => {
  const response = {
    output: [{
      content: [{
        type: "output_text",
        text: JSON.stringify({
          action: "HOLD",
          suggested_lots: 0,
          confidence: 0.2,
          summary: "信号较弱",
          reasoning: "指标冲突",
          risk_warning: "方向不确定",
        }),
      }],
    }],
  };

  assert.throws(
    () => parseOpenAiAdvice(response, "forced_directional"),
    /Invalid action/,
  );
});
