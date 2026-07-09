using System;

namespace TestFXTrade.Fx.Domain
{
    [Serializable]
    public sealed class PositionSnapshot
    {
        public double LongLots;
        public double ShortLots;
        public double AverageLongEntry;
        public double AverageShortEntry;

        public PositionSnapshot(double longLots, double shortLots, double averageLongEntry, double averageShortEntry)
        {
            LongLots = Math.Max(0d, longLots);
            ShortLots = Math.Max(0d, shortLots);
            AverageLongEntry = Math.Max(0d, averageLongEntry);
            AverageShortEntry = Math.Max(0d, averageShortEntry);
        }

        public double NetLots => LongLots - ShortLots;
        public double GrossLots => LongLots + ShortLots;
    }
}
