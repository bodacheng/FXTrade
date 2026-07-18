using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestFXTrade.Fx.MarketData;
using UnityEngine;
using UnityEngine.Networking;

namespace TestFXTrade.Fx.OpenAI
{
    public sealed class AzureRelayTradeAdvisorClient
    {
        private readonly string relayBaseUrl;

        public AzureRelayTradeAdvisorClient(string relayBaseUrl)
        {
            this.relayBaseUrl = AzureRelaySettings.NormalizeBaseUrl(relayBaseUrl);
        }

        public Task<OpenAiTradeAdvice> GetAdviceAsync(string prompt, CancellationToken cancellationToken)
        {
            return GetAdviceAsync(prompt, AiTradeAdviceMode.Conservative, cancellationToken);
        }

        public async Task<OpenAiTradeAdvice> GetAdviceAsync(
            string prompt,
            AiTradeAdviceMode mode,
            CancellationToken cancellationToken)
        {
            return await GetAdviceAsync(prompt, mode, "Simplified Chinese", cancellationToken);
        }

        public async Task<OpenAiTradeAdvice> GetAdviceAsync(
            string prompt,
            AiTradeAdviceMode mode,
            string responseLanguage,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(relayBaseUrl))
            {
                throw new InvalidOperationException("AI 服务暂不可用，请稍后重试。");
            }

            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new ArgumentException("发送给AI的提示词为空。", nameof(prompt));
            }

            AzureRelayAdviceRequest payload = BuildRequest(prompt, mode, responseLanguage);
            string requestJson = JsonUtility.ToJson(payload);
            string endpoint = $"{relayBaseUrl}/api/advice";

            using UnityWebRequest request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST);
            request.timeout = 75;
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestJson));
            request.downloadHandler = new DownloadHandlerBuffer();
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
                throw new InvalidOperationException(ReadRelayError(responseJson, request.error));
            }

            return ParseResponse(responseJson, mode);
        }

        public static OpenAiTradeAdvice ParseResponse(string responseJson)
        {
            return ParseResponse(responseJson, AiTradeAdviceMode.Conservative);
        }

        public static OpenAiTradeAdvice ParseResponse(string responseJson, AiTradeAdviceMode mode)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                throw new InvalidOperationException("AI 服务返回了空响应。");
            }

            OpenAiTradeAdvice advice = JsonUtility.FromJson<OpenAiTradeAdvice>(responseJson);
            ValidateAdvice(advice, mode);
            return advice;
        }

        private static AzureRelayAdviceRequest BuildRequest(string prompt, AiTradeAdviceMode mode)
        {
            return BuildRequest(prompt, mode, "Simplified Chinese");
        }

        private static AzureRelayAdviceRequest BuildRequest(
            string prompt,
            AiTradeAdviceMode mode,
            string responseLanguage)
        {
            return new AzureRelayAdviceRequest
            {
                prompt = prompt,
                mode = mode == AiTradeAdviceMode.ForcedDirectional ? "forced_directional" : "conservative",
                language = NormalizeResponseLanguage(responseLanguage)
            };
        }

        private static string NormalizeResponseLanguage(string responseLanguage)
        {
            switch (responseLanguage)
            {
                case "English":
                case "Japanese":
                    return responseLanguage;
                default:
                    return "Simplified Chinese";
            }
        }

        private static void ValidateAdvice(OpenAiTradeAdvice advice, AiTradeAdviceMode mode)
        {
            if (advice == null)
            {
                throw new InvalidOperationException("AI 服务返回的建议内容为空。");
            }

            string action = (advice.action ?? string.Empty).Trim().ToUpperInvariant();
            if (action != "BUY" && action != "SELL" && action != "HOLD")
            {
                throw new InvalidOperationException("AI返回了无法识别的交易动作。");
            }

            if (mode == AiTradeAdviceMode.ForcedDirectional && action == "HOLD")
            {
                throw new InvalidOperationException("积极策略必须在买入和卖出中选择一个方向。");
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

        private static string ReadRelayError(string responseJson, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(responseJson))
            {
                try
                {
                    AzureRelayErrorResponse response = JsonUtility.FromJson<AzureRelayErrorResponse>(responseJson);
                    if (!string.IsNullOrWhiteSpace(response?.error))
                    {
                        return $"AI 服务请求失败：{response.error}";
                    }
                }
                catch (Exception)
                {
                }
            }

            return $"AI 服务请求失败：{fallback}";
        }

        [Serializable]
        private sealed class AzureRelayAdviceRequest
        {
            public string prompt;
            public string mode;
            public string language;
        }

        [Serializable]
        private sealed class AzureRelayErrorResponse
        {
            public string error;
        }
    }
}
