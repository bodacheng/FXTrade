using System;

namespace TestFXTrade.Fx.Domain
{
    [Serializable]
    public sealed class Candle
    {
        public DateTime TimeUtc;
        public double Open;
        public double High;
        public double Low;
        public double Close;

        public Candle(DateTime timeUtc, double open, double high, double low, double close)
        {
            TimeUtc = timeUtc;
            Open = open;
            High = high;
            Low = low;
            Close = close;
        }
    }
}
