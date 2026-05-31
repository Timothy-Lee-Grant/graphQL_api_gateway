using System.Text.Json;
using StackExchange.Redis;

namespace GraphQLGateway.Services;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan ttl) where T : class;
    Task<bool> ExistsAsync(string key);
    Task<CacheStats> GetStatsAsync();
}

public record CacheStats(long TotalKeys, long HitCount, long MissCount)
{
    public double HitRate => (HitCount + MissCount) == 0 ? 0 : Math.Round((double)HitCount / (HitCount + MissCount) * 100, 1);
}

public class RedisCacheService(IConnectionMultiplexer redis, ILogger<RedisCacheService> logger) : ICacheService
{
    private readonly IDatabase _db = redis.GetDatabase();
    private long _hits = 0;
    private long _misses = 0;

    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        try
        {
            var value = await _db.StringGetAsync(key);
            if (value.IsNullOrEmpty)
            {
                Interlocked.Increment(ref _misses);
                return null;
            }
            Interlocked.Increment(ref _hits);
            return JsonSerializer.Deserialize<T>(value!);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache GET failed for key {Key}", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl) where T : class
    {
        try
        {
            var json = JsonSerializer.Serialize(value);
            await _db.StringSetAsync(key, json, ttl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cache SET failed for key {Key}", key);
        }
    }

    public async Task<bool> ExistsAsync(string key)
    {
        try { return await _db.KeyExistsAsync(key); }
        catch { return false; }
    }

    public async Task<CacheStats> GetStatsAsync()
    {
        try
        {
            var server = redis.GetServer(redis.GetEndPoints().First());
            var keyCount = await server.DatabaseSizeAsync();
            return new CacheStats(keyCount, _hits, _misses);
        }
        catch
        {
            return new CacheStats(0, _hits, _misses);
        }
    }
}

// No-op cache for when Redis isn't available
public class NullCacheService : ICacheService
{
    public Task<T?> GetAsync<T>(string key) where T : class => Task.FromResult<T?>(null);
    public Task SetAsync<T>(string key, T value, TimeSpan ttl) where T : class => Task.CompletedTask;
    public Task<bool> ExistsAsync(string key) => Task.FromResult(false);
    public Task<CacheStats> GetStatsAsync() => Task.FromResult(new CacheStats(0, 0, 0));
}
