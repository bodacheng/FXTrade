using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestFXTrade.Fx.Domain;

namespace TestFXTrade.Fx.MarketData
{
    public interface IFxMarketDataProvider
    {
        string ProviderName { get; }
        Task<MarketQuote> GetLatestQuoteAsync(string symbol, CancellationToken cancellationToken);
        Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, string interval, int outputSize, CancellationToken cancellationToken);
    }
}
