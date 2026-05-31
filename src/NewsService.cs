using System.Text.Json;
using GraphQLGateway.Types;

namespace GraphQLGateway.Services;

public interface INewsService
{
    Task<IEnumerable<NewsArticle>> GetTopHeadlinesAsync(string? query = null, string? category = null, int pageSize = 10, CancellationToken ct = default);
    Task<IEnumerable<NewsArticle>> SearchAsync(string query, int pageSize = 10, CancellationToken ct = default);
}

public class NewsService(HttpClient http, IConfiguration config, ILogger<NewsService> logger) : INewsService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private string ApiKey => config["NewsApi:ApiKey"] ?? "demo";

    public async Task<IEnumerable<NewsArticle>> GetTopHeadlinesAsync(
        string? query = null,
        string? category = null,
        int pageSize = 10,
        CancellationToken ct = default)
    {
        try
        {
            var qs = new List<string>
            {
                $"apiKey={ApiKey}",
                $"pageSize={pageSize}",
                "language=en"
            };
            if (!string.IsNullOrEmpty(query)) qs.Add($"q={Uri.EscapeDataString(query)}");
            if (!string.IsNullOrEmpty(category)) qs.Add($"category={category}");
            else qs.Add("sources=bbc-news,reuters,associated-press");

            var url = $"https://newsapi.org/v2/top-headlines?{string.Join("&", qs)}";
            var resp = await http.GetStringAsync(url, ct);
            return ParseArticles(resp);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch headlines");
            return GetMockNews(query ?? "world");
        }
    }

    public async Task<IEnumerable<NewsArticle>> SearchAsync(string query, int pageSize = 10, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://newsapi.org/v2/everything?q={Uri.EscapeDataString(query)}&pageSize={pageSize}&language=en&sortBy=publishedAt&apiKey={ApiKey}";
            var resp = await http.GetStringAsync(url, ct);
            return ParseArticles(resp);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search news for {Query}", query);
            return GetMockNews(query);
        }
    }

    private static IEnumerable<NewsArticle> ParseArticles(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("articles", out var articles)) return [];

        return articles.EnumerateArray().Select(a => new NewsArticle(
            Title: a.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
            Description: a.TryGetProperty("description", out var d) ? d.GetString() : null,
            Url: a.TryGetProperty("url", out var u) ? u.GetString() : null,
            Source: a.TryGetProperty("source", out var s) && s.TryGetProperty("name", out var sn) ? sn.GetString() : null,
            PublishedAt: a.TryGetProperty("publishedAt", out var p) ? p.GetString() : null,
            UrlToImage: a.TryGetProperty("urlToImage", out var img) ? img.GetString() : null
        )).Where(a => !string.IsNullOrEmpty(a.Title) && a.Title != "[Removed]").ToList();
    }

    // Fallback mock data when API key is not set
    private static IEnumerable<NewsArticle> GetMockNews(string query) =>
    [
        new($"Global Markets React to {query} Developments", "Financial analysts weigh in on the latest trends affecting markets worldwide.", "https://example.com/1", "Reuters", DateTime.UtcNow.AddHours(-1).ToString("o"), null),
        new($"Tech Giants Announce New {query} Initiatives", "Major technology companies unveil plans addressing key industry challenges.", "https://example.com/2", "BBC News", DateTime.UtcNow.AddHours(-3).ToString("o"), null),
        new($"Scientists Discover New Insights Related to {query}", "Research teams publish findings that could reshape understanding of the field.", "https://example.com/3", "Associated Press", DateTime.UtcNow.AddHours(-6).ToString("o"), null),
        new($"Policy Makers Meet to Discuss {query} Strategy", "World leaders gather for summit focused on collaborative solutions.", "https://example.com/4", "The Guardian", DateTime.UtcNow.AddHours(-8).ToString("o"), null),
        new($"Community Leaders Rally Around {query} Cause", "Grassroots movements gain momentum as local organizations unite.", "https://example.com/5", "NPR", DateTime.UtcNow.AddHours(-12).ToString("o"), null),
    ];
}
