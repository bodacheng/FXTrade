using System;

namespace TestFXTrade.Fx.Domain
{
    [Serializable]
    public sealed class RiskProfile
    {
        public double RiskPercentPerTrade;
        public double MaxMarginUsagePercent;
        public double PlannedStopLossPips;
        public double EstimatedSpreadPips;

        public RiskProfile(double riskPercentPerTrade, double maxMarginUsagePercent, double plannedStopLossPips, double estimatedSpreadPips)
        {
            RiskPercentPerTrade = Math.Max(0d, riskPercentPerTrade);
            MaxMarginUsagePercent = Math.Max(0d, maxMarginUsagePercent);
            PlannedStopLossPips = Math.Max(1d, plannedStopLossPips);
            EstimatedSpreadPips = Math.Max(0d, estimatedSpreadPips);
        }
    }
}
