using Microsoft.Extensions.Caching.Distributed;

namespace DictionaryImporter.AITextKit.AI.Infrastructure.Implementations;

public class DistributedResponseCache(
    IDistributedCache distributedCache,
    IOptions<CacheConfiguration> config,
    ILogger<DistributedResponseCache> logger)
    : IResponseCache
{
    private readonly IDistributedCache _distributedCache = distributedCache ?? throw new ArgumentNullException(nameof(distributedCache));
    private readonly CacheConfiguration _config = config.Value;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public async Task<CachedResponse> GetCachedResponseAsync(string cacheKey)
    {
        try
        {
            var cachedData = await _distributedCache.GetStringAsync(cacheKey);

            if (string.IsNullOrEmpty(cachedData))
            {
                logger.LogDebug("Cache miss for key: {CacheKey}", cacheKey);
                return null;
            }

            logger.LogDebug("Cache hit for key: {CacheKey}", cacheKey);

            var cachedResponse = JsonSerializer.Deserialize<CachedResponse>(cachedData, _jsonOptions);

            cachedResponse.LastAccessedAt = DateTime.UtcNow;
            cachedResponse.HitCount++;

            await SetCachedResponseAsync(cacheKey, cachedResponse,
                cachedResponse.ExpiresAt - DateTime.UtcNow);

            return cachedResponse;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get cached response for key: {CacheKey}", cacheKey);
            return null;
        }
    }

    public async Task SetCachedResponseAsync(string cacheKey, CachedResponse response, TimeSpan ttl)
    {
        try
        {
            response.LastAccessedAt = DateTime.UtcNow;

            var serialized = JsonSerializer.Serialize(response, _jsonOptions);

            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            };

            await _distributedCache.SetStringAsync(cacheKey, serialized, options);

            logger.LogDebug("Cached response for key: {CacheKey} with TTL: {TTL} seconds",
                cacheKey, ttl.TotalSeconds);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to cache response for key: {CacheKey}", cacheKey);
        }
    }

    public async Task RemoveCachedResponseAsync(string cacheKey)
    {
        try
        {
            await _distributedCache.RemoveAsync(cacheKey);
            logger.LogDebug("Removed cached response for key: {CacheKey}", cacheKey);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to remove cached response for key: {CacheKey}", cacheKey);
        }
    }

    public async Task CleanExpiredCacheAsync()
    {
        try
        {
            logger.LogDebug("CleanExpiredCacheAsync called - distributed cache handles expiration automatically");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to clean expired cache");
        }
    }
}