using System;
using TestFXTrade.Fx.Domain;

namespace TestFXTrade.Fx.Analysis
{
    public static class RiskCalculationService
    {
        public static double GetPipValuePerLot(AccountCurrency accountCurrency, double usdJpyPrice)
        {
            if (accountCurrency == AccountCurrency.Jpy)
            {
                return FxConstants.UsdJpyPipValueJpyPerLot;
            }

            return usdJpyPrice > 0d ? FxConstants.UsdJpyPipValueJpyPerLot / usdJpyPrice : 0d;
        }

        public static double GetMarginPerLot(AccountCurrency accountCurrency, double usdJpyPrice, double leverage)
        {
            leverage = Math.Max(1d, leverage);
            double marginUsd = FxConstants.StandardLotBaseUnits / leverage;

            if (accountCurrency == AccountCurrency.Usd)
            {
                return marginUsd;
            }

            return marginUsd * Math.Max(0d, usdJpyPrice);
        }

        public static double GetSafeLotsByStop(AccountSnapshot account, RiskProfile risk, double pipValuePerLot)
        {
            if (account == null || risk == null || pipValuePerLot <= 0d)
            {
                return 0d;
            }

            double riskBudget = account.Equity * (risk.RiskPercentPerTrade / 100d);
            double lossPerLot = risk.PlannedStopLossPips * pipValuePerLot;
            return lossPerLot > 0d ? riskBudget / lossPerLot : 0d;
        }

        public static double GetSafeLotsByMargin(AccountSnapshot account, PositionSnapshot position, RiskProfile risk, double marginPerLot)
        {
            if (account == null || position == null || risk == null || marginPerLot <= 0d)
            {
                return 0d;
            }

            double maxMarginBudget = account.Equity * (risk.MaxMarginUsagePercent / 100d);
            double currentMargin = position.GrossLots * marginPerLot;
            double availableMarginBudget = Math.Max(0d, maxMarginBudget - currentMargin);
            return availableMarginBudget / marginPerLot;
        }
    }
}
