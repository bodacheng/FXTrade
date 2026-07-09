using System;

namespace TestFXTrade.Fx.Domain
{
    [Serializable]
    public sealed class AccountSnapshot
    {
        public double Principal;
        public double Equity;
        public AccountCurrency Currency;
        public double Leverage;

        public AccountSnapshot(double principal, double equity, AccountCurrency currency, double leverage)
        {
            Principal = Math.Max(0d, principal);
            Equity = Math.Max(0d, equity);
            Currency = currency;
            Leverage = Math.Max(1d, leverage);
        }
    }

    public enum AccountCurrency
    {
        Jpy,
        Usd
    }
}
