using System.Text.Json;
using GraphQLGateway.Types;

namespace GraphQLGateway.Services;

public interface IFinanceService
{
    Task<StockQuote?> GetQuoteAsync(string ticker, CancellationToken ct = default);
    Task<IEnumerable<StockQuote>> GetMultipleQuotesAsync(IEnumerable<string> tickers, CancellationToken ct = default);
}

public class FinanceService(HttpClient http, IConfiguration config, ILogger<FinanceService> logger) : IFinanceService
{
    private string ApiKey => config["AlphaVantage:ApiKey"] ?? "demo";

    public async Task<StockQuote?> GetQuoteAsync(string ticker, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://www.alphavantage.co/query?function=GLOBAL_QUOTE&symbol={Uri.EscapeDataString(ticker)}&apikey={ApiKey}";
            var resp = await http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(resp);

            if (!doc.RootElement.TryGetProperty("Global Quote", out var quote)) return GetMockQuote(ticker);
            if (!quote.TryGetProperty("05. price", out var price)) return GetMockQuote(ticker);

            double.TryParse(quote.GetProperty("05. price").GetString(), out var p);
            double.TryParse(quote.GetProperty("09. change").GetString(), out var change);
            double.TryParse(quote.GetProperty("10. change percent").GetString()?.TrimEnd('%'), out var changePct);
            double.TryParse(quote.GetProperty("03. high").GetString(), out var high);
            double.TryParse(quote.GetProperty("04. low").GetString(), out var low);
            long.TryParse(quote.GetProperty("06. volume").GetString(), out var volume);

            return new StockQuote(
                Ticker: ticker.ToUpper(),
                CompanyName: ticker.ToUpper(),
                Price: p,
                Change: change,
                ChangePercent: changePct,
                High: high,
                Low: low,
                Volume: volume,
                LastUpdated: quote.GetProperty("07. latest trading day").GetString() ?? DateTime.UtcNow.ToString("o")
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Finance API failed for {Ticker}, using mock data", ticker);
            return GetMockQuote(ticker);
        }
    }

    public async Task<IEnumerable<StockQuote>> GetMultipleQuotesAsync(IEnumerable<string> tickers, CancellationToken ct = default)
    {
        var tasks = tickers.Select(t => GetQuoteAsync(t, ct));
        var results = await Task.WhenAll(tasks);
        return results.Where(q => q is not null).Cast<StockQuote>();
    }

    private static readonly Dictionary<string, (string Name, double BasePrice)> KnownStocks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AAPL"] = ("Apple Inc.", 189.25),
        ["MSFT"] = ("Microsoft Corporation", 415.50),
        ["GOOGL"] = ("Alphabet Inc.", 175.80),
        ["AMZN"] = ("Amazon.com Inc.", 201.40),
        ["META"] = ("Meta Platforms Inc.", 556.20),
        ["NVDA"] = ("NVIDIA Corporation", 875.60),
        ["TSLA"] = ("Tesla Inc.", 248.90),
        ["NFLX"] = ("Netflix Inc.", 745.30),
    };

    private static StockQuote GetMockQuote(string ticker)
    {
        var rng = new Random(ticker.GetHashCode());
        var (name, basePrice) = KnownStocks.TryGetValue(ticker, out var known)
            ? known
            : ($"{ticker.ToUpper()} Corp.", 100.0 + rng.NextDouble() * 500);

        var variation = (rng.NextDouble() - 0.5) * 0.04;
        var price = Math.Round(basePrice * (1 + variation), 2);
        var change = Math.Round(price - basePrice, 2);
        var changePct = Math.Round(change / basePrice * 100, 2);

        return new StockQuote(
            Ticker: ticker.ToUpper(),
            CompanyName: name,
            Price: price,
            Change: change,
            ChangePercent: changePct,
            High: Math.Round(price * 1.02, 2),
            Low: Math.Round(price * 0.98, 2),
            Volume: rng.Next(1_000_000, 50_000_000),
            LastUpdated: DateTime.UtcNow.ToString("o")
        );
    }
}
