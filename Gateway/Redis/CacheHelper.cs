using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DictionaryImporter.Gateway.Redis
{
    public static class CacheHelper
    {
        public static async Task<T> GetOrLoadAsync<T>(
            string key,
            TimeSpan ttl,
            Func<CancellationToken, Task<T>> loader,
            IDistributedCacheStore? cache,
            CancellationToken ct)
        {
            if (cache is not null)
            {
                var cached = await cache.GetAsync<T>(key, ct);
                if (cached is not null)
                    return cached;
            }

            var value = await loader(ct);

            if (cache is not null)
                await cache.SetAsync(key, value, ttl, ct);

            return value;
        }
    }
}