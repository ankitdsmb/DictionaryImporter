namespace DictionaryImporter.AITextKit.AI.Infrastructure;

public interface IResponseCache
{
    Task<CachedResponse> GetCachedResponseAsync(string cacheKey);

    Task SetCachedResponseAsync(string cacheKey, CachedResponse response, TimeSpan ttl);

    Task RemoveCachedResponseAsync(string cacheKey);

    Task CleanExpiredCacheAsync();
}