using System;
using System.Collections.Generic;
using TestFXTrade.Fx.Domain;

namespace TestFXTrade.Fx.Analysis
{
    public static class TechnicalIndicatorService
    {
        public static double CalculateEma(IReadOnlyList<Candle> candles, int period)
        {
            if (candles == null || candles.Count == 0)
            {
                return 0d;
            }

            period = Math.Max(1, period);
            double multiplier = 2d / (period + 1d);
            double ema = candles[0].Close;

            for (int i = 1; i < candles.Count; i++)
            {
                ema = ((candles[i].Close - ema) * multiplier) + ema;
            }

            return ema;
        }

        public static double CalculateRsi(IReadOnlyList<Candle> candles, int period)
        {
            if (candles == null || candles.Count <= period)
            {
                return 50d;
            }

            double gain = 0d;
            double loss = 0d;
            int start = candles.Count - period;

            for (int i = start; i < candles.Count; i++)
            {
                double change = candles[i].Close - candles[i - 1].Close;
                if (change >= 0d)
                {
                    gain += change;
                }
                else
                {
                    loss -= change;
                }
            }

            if (loss <= 0d)
            {
                return 100d;
            }

            double rs = gain / loss;
            return 100d - (100d / (1d + rs));
        }

        public static double CalculateAtrPips(IReadOnlyList<Candle> candles, int period)
        {
            if (candles == null || candles.Count <= 1)
            {
                return 0d;
            }

            period = Math.Min(period, candles.Count - 1);
            int start = candles.Count - period;
            double totalTrueRange = 0d;

            for (int i = start; i < candles.Count; i++)
            {
                Candle current = candles[i];
                Candle previous = candles[i - 1];
                double highLow = current.High - current.Low;
                double highClose = Math.Abs(current.High - previous.Close);
                double lowClose = Math.Abs(current.Low - previous.Close);
                totalTrueRange += Math.Max(highLow, Math.Max(highClose, lowClose));
            }

            return (totalTrueRange / period) / FxConstants.UsdJpyPipSize;
        }

        public static double CalculateTrendScore(IReadOnlyList<Candle> candles, out double atrPips, out double rsi)
        {
            atrPips = CalculateAtrPips(candles, 14);
            rsi = CalculateRsi(candles, 14);

            if (candles == null || candles.Count < 60 || atrPips <= 0d)
            {
                return 0d;
            }

            double emaFast = CalculateEma(candles, 12);
            double emaSlow = CalculateEma(candles, 26);
            double latest = candles[candles.Count - 1].Close;
            double earlier = candles[Math.Max(0, candles.Count - 13)].Close;
            double atrPrice = atrPips * FxConstants.UsdJpyPipSize;

            double emaComponent = Clamp((emaFast - emaSlow) / (atrPrice * 2d), -1d, 1d) * 0.4d;
            double momentumComponent = Clamp((latest - earlier) / (atrPrice * 3d), -1d, 1d) * 0.3d;
            double rsiComponent = Clamp((rsi - 50d) / 25d, -1d, 1d) * 0.2d;
            double slopeComponent = Clamp(CalculateSlopeComponent(candles, atrPrice), -1d, 1d) * 0.1d;

            return Clamp(emaComponent + momentumComponent + rsiComponent + slopeComponent, -1d, 1d);
        }

        private static double CalculateSlopeComponent(IReadOnlyList<Candle> candles, double atrPrice)
        {
            const int lookback = 30;
            if (candles.Count < lookback || atrPrice <= 0d)
            {
                return 0d;
            }

            int start = candles.Count - lookback;
            double sumX = 0d;
            double sumY = 0d;
            double sumXY = 0d;
            double sumXX = 0d;

            for (int i = 0; i < lookback; i++)
            {
                double x = i;
                double y = candles[start + i].Close;
                sumX += x;
                sumY += y;
                sumXY += x * y;
                sumXX += x * x;
            }

            double denominator = (lookback * sumXX) - (sumX * sumX);
            if (Math.Abs(denominator) < double.Epsilon)
            {
                return 0d;
            }

            double slope = ((lookback * sumXY) - (sumX * sumY)) / denominator;
            return slope / (atrPrice / lookback);
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
