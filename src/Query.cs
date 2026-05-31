using GraphQLGateway.Services;
using GraphQLGateway.Types;

namespace GraphQLGateway.GraphQL;

[QueryType]
public class Query
{
    private static readonly TimeSpan WeatherTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan NewsTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StockTtl = TimeSpan.FromSeconds(30);

    // ─── Weather ────────────────────────────────────────────────────────────

    [GraphQLDescription("Get current weather conditions for a city.")]
    public async Task<WeatherData?> GetWeather(
        string city,
        [Service] IWeatherService weather,
        [Service] ICacheService cache)
    {
        var key = $"weather:{city.ToLower().Trim()}";
        var cached = await cache.GetAsync<WeatherData>(key);
        if (cached is not null) return cached;

        var result = await weather.GetCurrentAsync(city);
        if (result is not null) await cache.SetAsync(key, result, WeatherTtl);
        return result;
    }

    [GraphQLDescription("Get multi-day weather forecast for a city.")]
    public async Task<IEnumerable<WeatherForecastDay>> GetForecast(
        string city,
        int days = 5,
        [Service] IWeatherService weather,
        [Service] ICacheService cache)
    {
        var key = $"forecast:{city.ToLower().Trim()}:{days}";
        var cached = await cache.GetAsync<List<WeatherForecastDay>>(key);
        if (cached is not null) return cached;

        var result = (await weather.GetForecastAsync(city, days)).ToList();
        if (result.Any()) await cache.SetAsync(key, result, WeatherTtl);
        return result;
    }

    // ─── News ───────────────────────────────────────────────────────────────

    [GraphQLDescription("Get top news headlines, optionally filtered by query or category.")]
    public async Task<IEnumerable<NewsArticle>> GetTopHeadlines(
        string? query = null,
        string? category = null,
        int pageSize = 10,
        [Service] INewsService news,
        [Service] ICacheService cache)
    {
        var key = $"headlines:{query?.ToLower() ?? "top"}:{category ?? "all"}:{pageSize}";
        var cached = await cache.GetAsync<List<NewsArticle>>(key);
        if (cached is not null) return cached;

        var result = (await news.GetTopHeadlinesAsync(query, category, pageSize)).ToList();
        if (result.Any()) await cache.SetAsync(key, result, NewsTtl);
        return result;
    }

    [GraphQLDescription("Search news articles by keyword.")]
    public async Task<IEnumerable<NewsArticle>> SearchNews(
        string query,
        int pageSize = 10,
        [Service] INewsService news,
        [Service] ICacheService cache)
    {
        var key = $"news-search:{query.ToLower().Trim()}:{pageSize}";
        var cached = await cache.GetAsync<List<NewsArticle>>(key);
        if (cached is not null) return cached;

        var result = (await news.SearchAsync(query, pageSize)).ToList();
        if (result.Any()) await cache.SetAsync(key, result, NewsTtl);
        return result;
    }

    // ─── Finance ────────────────────────────────────────────────────────────

    [GraphQLDescription("Get real-time stock quote for a ticker symbol.")]
    public async Task<StockQuote?> GetStockQuote(
        string ticker,
        [Service] IFinanceService finance,
        [Service] ICacheService cache)
    {
        var key = $"stock:{ticker.ToUpper().Trim()}";
        var cached = await cache.GetAsync<StockQuote>(key);
        if (cached is not null) return cached;

        var result = await finance.GetQuoteAsync(ticker);
        if (result is not null) await cache.SetAsync(key, result, StockTtl);
        return result;
    }

    [GraphQLDescription("Get quotes for multiple ticker symbols in parallel.")]
    public async Task<IEnumerable<StockQuote>> GetMultipleQuotes(
        IEnumerable<string> tickers,
        [Service] IFinanceService finance,
        [Service] ICacheService cache)
    {
        var tickerList = tickers.ToList();
        var tasks = tickerList.Select(async t =>
        {
            var key = $"stock:{t.ToUpper().Trim()}";
            var cached = await cache.GetAsync<StockQuote>(key);
            if (cached is not null) return cached;

            var result = await finance.GetQuoteAsync(t);
            if (result is not null) await cache.SetAsync(key, result, StockTtl);
            return result;
        });

        var results = await Task.WhenAll(tasks);
        return results.Where(q => q is not null).Cast<StockQuote>();
    }

    // ─── Aggregated ─────────────────────────────────────────────────────────

    [GraphQLDescription("Get a city dashboard combining weather, local news, and an optional stock quote in one query.")]
    public async Task<CityDashboard> GetCityDashboard(
        string city,
        string? stockTicker = null,
        int newsCount = 5,
        [Service] IWeatherService weather,
        [Service] INewsService news,
        [Service] IFinanceService finance,
        [Service] ICacheService cache)
    {
        var weatherTask = GetWeather(city, weather, cache);
        var newsTask = GetTopHeadlines(city, null, newsCount, news, cache);
        var stockTask = stockTicker is not null ? GetStockQuote(stockTicker, finance, cache) : Task.FromResult<StockQuote?>(null);

        await Task.WhenAll(weatherTask, newsTask, stockTask);

        return new CityDashboard(
            Weather: await weatherTask,
            News: await newsTask,
            Stock: await stockTask
        );
    }

    // ─── Meta ────────────────────────────────────────────────────────────────

    [GraphQLDescription("Get cache performance statistics.")]
    public async Task<CacheStats> GetCacheStats([Service] ICacheService cache)
        => await cache.GetStatsAsync();
}
