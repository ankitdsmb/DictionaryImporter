using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DictionaryImporter.AI.Infrastructure.Implementations
{
    public class NullResponseCache(ILogger<NullResponseCache> logger = null) : IResponseCache
    {
        public Task<CachedResponse> GetCachedResponseAsync(string cacheKey)
        {
            logger?.LogDebug("NullResponseCache: Cache miss for key {CacheKey}", cacheKey);
            return Task.FromResult<CachedResponse>(null);
        }

        public Task SetCachedResponseAsync(string cacheKey, CachedResponse response, TimeSpan ttl)
        {
            logger?.LogDebug("NullResponseCache: Would cache response for key {CacheKey}", cacheKey);
            return Task.CompletedTask;
        }

        public Task RemoveCachedResponseAsync(string cacheKey)
        {
            logger?.LogDebug("NullResponseCache: Would remove cache for key {CacheKey}", cacheKey);
            return Task.CompletedTask;
        }

        public Task CleanExpiredCacheAsync()
        {
            return Task.CompletedTask;
        }
    }
}