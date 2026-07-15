using System;
using System.Globalization;

namespace TestFXTrade.Fx.Sbi
{
    [Serializable]
    public sealed class SbiFxRuleSnapshot
    {
        public string SourceUrl;
        public string FetchedAtUtc;
        public string ApplicableDate;
        public string Pair;
        public int Leverage;
        public double MarginRatePercent;
        public int RequiredMarginPer10000Jpy;
        public int MinimumOrderUnits;

        public bool IsUsable =>
            !string.IsNullOrWhiteSpace(SourceUrl) &&
            string.Equals(Pair, "USD/JPY", StringComparison.Ordinal) &&
            Leverage > 0 &&
            MarginRatePercent > 0d &&
            RequiredMarginPer10000Jpy > 0 &&
            MinimumOrderUnits > 0;

        public double RequiredMarginPerStandardLotJpy => RequiredMarginPer10000Jpy * 10d;

        public string ToPromptText()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "SBI FX official local snapshot: pair={0}; leverage={1}x; margin_rate={2:0.##}%; " +
                "required_margin_per_10,000_currency={3} JPY; minimum_order={4} currency; " +
                "applicable_date={5}; fetched_at_utc={6}; source={7}.",
                Pair,
                Leverage,
                MarginRatePercent,
                RequiredMarginPer10000Jpy,
                MinimumOrderUnits,
                ApplicableDate,
                FetchedAtUtc,
                SourceUrl);
        }
    }
}
