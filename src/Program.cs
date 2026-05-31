using AspNetCoreRateLimit;
using GraphQLGateway.Services;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ─── Logging ────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();
builder.Host.UseSerilog();

// ─── HTTP Clients ────────────────────────────────────────────────────────────
builder.Services.AddHttpClient<IWeatherService, WeatherService>()
    .SetHandlerLifetime(TimeSpan.FromMinutes(5));
builder.Services.AddHttpClient<INewsService, NewsService>()
    .SetHandlerLifetime(TimeSpan.FromMinutes(5));
builder.Services.AddHttpClient<IFinanceService, FinanceService>()
    .SetHandlerLifetime(TimeSpan.FromMinutes(5));

// ─── Redis Cache ─────────────────────────────────────────────────────────────
var redisConn = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
try
{
    var multiplexer = ConnectionMultiplexer.Connect(redisConn);
    builder.Services.AddSingleton<IConnectionMultiplexer>(multiplexer);
    builder.Services.AddSingleton<ICacheService, RedisCacheService>();
    Log.Information("Redis connected at {Redis}", redisConn);
}
catch
{
    Log.Warning("Redis unavailable at {Redis} — running without cache", redisConn);
    builder.Services.AddSingleton<ICacheService, NullCacheService>();
}

// ─── Rate Limiting ───────────────────────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
builder.Services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
builder.Services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();
builder.Services.AddInMemoryRateLimiting();

// ─── GraphQL ─────────────────────────────────────────────────────────────────
builder.Services
    .AddGraphQLServer()
    .AddQueryType<GraphQLGateway.GraphQL.Query>()
    .AddInstrumentation()
    .ModifyRequestOptions(o => o.IncludeExceptionDetails = builder.Environment.IsDevelopment());

// ─── CORS (for React frontend) ───────────────────────────────────────────────
builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseCors();
app.UseIpRateLimiting();
app.UseSerilogRequestLogging();

// Redirect root to the playground
app.MapGet("/", () => Results.Redirect("/graphql"));

app.MapGraphQL("/graphql");

Log.Information("GraphQL Gateway running — Playground at http://localhost:5000/graphql");
app.Run();
