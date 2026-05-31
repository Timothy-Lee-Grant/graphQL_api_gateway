namespace GraphQLGateway.Types;

// ─── Weather ───────────────────────────────────────────────────────────────

public record WeatherData(
    string City,
    double Temperature,
    double FeelsLike,
    double WindSpeed,
    int Humidity,
    string Condition,
    string Icon,
    double Latitude,
    double Longitude
);

public record WeatherForecastDay(
    string Date,
    double MaxTemp,
    double MinTemp,
    string Condition
);

// ─── News ──────────────────────────────────────────────────────────────────

public record NewsArticle(
    string Title,
    string? Description,
    string? Url,
    string? Source,
    string? PublishedAt,
    string? UrlToImage
);

// ─── Finance ───────────────────────────────────────────────────────────────

public record StockQuote(
    string Ticker,
    string CompanyName,
    double Price,
    double Change,
    double ChangePercent,
    double High,
    double Low,
    long Volume,
    string LastUpdated
);

// ─── Aggregated Dashboard ──────────────────────────────────────────────────

public record CityDashboard(
    WeatherData? Weather,
    IEnumerable<NewsArticle> News,
    StockQuote? Stock
);
