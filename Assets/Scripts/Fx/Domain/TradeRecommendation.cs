using System;
using System.Collections.Generic;

namespace TestFXTrade.Fx.Domain
{
    [Serializable]
    public sealed class TradeRecommendation
    {
        public RecommendationAction Action;
        public double SuggestedBuyLots;
        public double SuggestedSellLots;
        public double TargetNetLots;
        public double CurrentNetLots;
        public double MaxSafeGrossLots;
        public double PipValuePerLotAccountCurrency;
        public double MarginPerLotAccountCurrency;
        public double RequiredMarginForSuggestion;
        public double TrendScore;
        public double Confidence;
        public double LatestPrice;
        public double AtrPips;
        public double Rsi;
        public string Summary;
        public readonly List<string> Reasons = new List<string>();
        public readonly List<string> Warnings = new List<string>();
    }

    public enum RecommendationAction
    {
        Hold,
        Buy,
        Sell
    }
}
