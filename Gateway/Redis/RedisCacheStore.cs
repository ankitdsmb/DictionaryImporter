using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DictionaryImporter.Gateway.Redis
{
    public sealed class RedisCacheStore : IDistributedCacheStore
    {
        private readonly IDatabase _db;
        private readonly IConnectionMultiplexer _mux;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public RedisCacheStore(IConnectionMultiplexer mux)
        {
            _mux = mux;
            _db = mux.GetDatabase();
        }

        public async Task<T?> GetAsync<T>(string key, CancellationToken ct)
        {
            var value = await _db.StringGetAsync(key);
            if (!value.HasValue)
                return default;

            return JsonSerializer.Deserialize<T>(value!, JsonOptions);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            return _db.StringSetAsync(key, json, ttl);
        }

        public Task RemoveAsync(string key, CancellationToken ct)
            => _db.KeyDeleteAsync(key);

        public async Task RemoveByPrefixAsync(string prefix, CancellationToken ct)
        {
            var server = _mux.GetServer(_mux.GetEndPoints()[0]);
            var keys = server.Keys(pattern: prefix + "*").ToArray();

            if (keys.Length > 0)
                await _db.KeyDeleteAsync(keys);
        }
    }
}