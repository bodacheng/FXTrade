using System;

namespace TestFXTrade.Fx.Domain
{
    [Serializable]
    public sealed class MarketQuote
    {
        public string Symbol;
        public double Price;
        public DateTime TimeUtc;
        public bool IsTimestampReliable;
        public string Source;

        public MarketQuote(string symbol, double price, DateTime timeUtc, bool isTimestampReliable, string source)
        {
            Symbol = symbol;
            Price = price;
            TimeUtc = timeUtc;
            IsTimestampReliable = isTimestampReliable;
            Source = source;
        }
    }
}
