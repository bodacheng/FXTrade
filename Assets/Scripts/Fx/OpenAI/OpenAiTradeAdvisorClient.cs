using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace TestFXTrade.Fx.OpenAI
{
    public sealed class OpenAiTradeAdvisorClient
    {
        public const string DefaultModel = "gpt-5.6";
        private const string ResponsesEndpoint = "https://api.openai.com/v1/responses";

        private readonly string apiKey;
        private readonly string model;

        public OpenAiTradeAdvisorClient(string apiKey, string model = DefaultModel)
        {
            this.apiKey = apiKey ?? string.Empty;
            this.model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
        }

        public async Task<OpenAiTradeAdvice> GetAdviceAsync(string prompt, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("请先在本地 .env 中配置 OPENAI_API_KEY。");
            }

            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new ArgumentException("发送给 OpenAI 的提示词为空。", nameof(prompt));
            }

            OpenAiResponseRequest payload = BuildRequest(prompt);
            string requestJson = JsonUtility.ToJson(payload);

            using UnityWebRequest request = new UnityWebRequest(ResponsesEndpoint, UnityWebRequest.kHttpVerbPOST);
            request.timeout = 60;
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestJson));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            request.SetRequestHeader("Content-Type", "application/json");

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            string responseJson = request.downloadHandler.text;
            if (request.result != UnityWebRequest.Result.Success)
            {
                string apiMessage = ReadApiError(responseJson);
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(apiMessage)
                    ? $"OpenAI API 请求失败：{request.error}"
                    : $"OpenAI API 请求失败：{apiMessage}");
            }

            return ParseResponse(responseJson);
        }

        public static OpenAiTradeAdvice ParseResponse(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                throw new InvalidOperationException("OpenAI API 返回了空响应。");
            }

            OpenAiResponse response = JsonUtility.FromJson<OpenAiResponse>(responseJson);
            if (response == null)
            {
                throw new InvalidOperationException("无法解析 OpenAI API 响应。");
            }

            if (response.error != null && !string.IsNullOrWhiteSpace(response.error.message))
            {
                throw new InvalidOperationException($"OpenAI API 错误：{response.error.message}");
            }

            if (response.output != null)
            {
                for (int i = 0; i < response.output.Length; i++)
                {
                    OpenAiOutputItem output = response.output[i];
                    if (output == null || output.content == null)
                    {
                        continue;
                    }

                    for (int j = 0; j < output.content.Length; j++)
                    {
                        OpenAiContentItem content = output.content[j];
                        if (content == null)
                        {
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(content.refusal))
                        {
                            throw new InvalidOperationException($"OpenAI 未生成建议：{content.refusal}");
                        }

                        if (string.Equals(content.type, "output_text", StringComparison.Ordinal) &&
                            !string.IsNullOrWhiteSpace(content.text))
                        {
                            OpenAiTradeAdvice advice = JsonUtility.FromJson<OpenAiTradeAdvice>(content.text);
                            ValidateAdvice(advice);
                            return advice;
                        }
                    }
                }
            }

            throw new InvalidOperationException("OpenAI API 响应中没有可用的结构化建议。");
        }

        private OpenAiResponseRequest BuildRequest(string prompt)
        {
            return new OpenAiResponseRequest
            {
                model = model,
                instructions = OpenAiTradePromptBuilder.Instructions,
                input = prompt,
                store = false,
                max_output_tokens = 700,
                text = new OpenAiTextConfiguration
                {
                    format = new OpenAiJsonSchemaFormat
                    {
                        type = "json_schema",
                        name = "fx_trade_advice",
                        strict = true,
                        schema = BuildAdviceSchema()
                    }
                }
            };
        }

        private static OpenAiJsonSchema BuildAdviceSchema()
        {
            return new OpenAiJsonSchema
            {
                type = "object",
                properties = new OpenAiAdviceProperties
                {
                    action = new OpenAiEnumSchemaProperty { type = "string", @enum = new[] { "BUY", "SELL", "HOLD" } },
                    suggested_lots = new OpenAiSchemaProperty { type = "number" },
                    confidence = new OpenAiSchemaProperty { type = "number" },
                    summary = new OpenAiSchemaProperty { type = "string" },
                    reasoning = new OpenAiSchemaProperty { type = "string" },
                    risk_warning = new OpenAiSchemaProperty { type = "string" }
                },
                required = new[]
                {
                    "action",
                    "suggested_lots",
                    "confidence",
                    "summary",
                    "reasoning",
                    "risk_warning"
                },
                additionalProperties = false
            };
        }

        private static void ValidateAdvice(OpenAiTradeAdvice advice)
        {
            if (advice == null)
            {
                throw new InvalidOperationException("OpenAI 返回的建议内容为空。");
            }

            string action = (advice.action ?? string.Empty).Trim().ToUpperInvariant();
            if (action != "BUY" && action != "SELL" && action != "HOLD")
            {
                throw new InvalidOperationException("OpenAI 返回了无法识别的交易动作。");
            }

            advice.action = action;
            advice.suggested_lots = Math.Max(0d, advice.suggested_lots);
            advice.confidence = Math.Max(0d, Math.Min(1d, advice.confidence));
            advice.summary = advice.summary ?? string.Empty;
            advice.reasoning = advice.reasoning ?? string.Empty;
            advice.risk_warning = advice.risk_warning ?? string.Empty;

            if (action == "HOLD")
            {
                advice.suggested_lots = 0d;
            }
        }

        private static string ReadApiError(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return string.Empty;
            }

            try
            {
                OpenAiResponse response = JsonUtility.FromJson<OpenAiResponse>(responseJson);
                return response?.error?.message ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        [Serializable]
        private sealed class OpenAiResponseRequest
        {
            public string model;
            public string instructions;
            public string input;
            public bool store;
            public int max_output_tokens;
            public OpenAiTextConfiguration text;
        }

        [Serializable]
        private sealed class OpenAiTextConfiguration
        {
            public OpenAiJsonSchemaFormat format;
        }

        [Serializable]
        private sealed class OpenAiJsonSchemaFormat
        {
            public string type;
            public string name;
            public bool strict;
            public OpenAiJsonSchema schema;
        }

        [Serializable]
        private sealed class OpenAiJsonSchema
        {
            public string type;
            public OpenAiAdviceProperties properties;
            public string[] required;
            public bool additionalProperties;
        }

        [Serializable]
        private sealed class OpenAiAdviceProperties
        {
            public OpenAiEnumSchemaProperty action;
            public OpenAiSchemaProperty suggested_lots;
            public OpenAiSchemaProperty confidence;
            public OpenAiSchemaProperty summary;
            public OpenAiSchemaProperty reasoning;
            public OpenAiSchemaProperty risk_warning;
        }

        [Serializable]
        private sealed class OpenAiSchemaProperty
        {
            public string type;
        }

        [Serializable]
        private sealed class OpenAiEnumSchemaProperty
        {
            public string type;
            public string[] @enum;
        }

        [Serializable]
        private sealed class OpenAiResponse
        {
            public OpenAiOutputItem[] output;
            public OpenAiError error;
        }

        [Serializable]
        private sealed class OpenAiOutputItem
        {
            public OpenAiContentItem[] content;
        }

        [Serializable]
        private sealed class OpenAiContentItem
        {
            public string type;
            public string text;
            public string refusal;
        }

        [Serializable]
        private sealed class OpenAiError
        {
            public string message;
        }
    }
}
