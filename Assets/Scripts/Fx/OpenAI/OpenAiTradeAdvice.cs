using System;

namespace TestFXTrade.Fx.OpenAI
{
    [Serializable]
    public sealed class OpenAiTradeAdvice
    {
        public string action;
        public double suggested_lots;
        public double confidence;
        public string summary;
        public string reasoning;
        public string risk_warning;
    }
}
