using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DictionaryImporter.AI.Infrastructure.Implementations
{
    public class RateLimiter(int limit, TimeSpan period)
    {
        private readonly ConcurrentQueue<DateTime> _requestTimes = new();
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public int Limit => limit;
        public TimeSpan RetryAfter => CalculateRetryAfter();

        public async Task<bool> WaitToProceedAsync(CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                var now = DateTime.UtcNow;
                var cutoff = now - period;

                while (_requestTimes.TryPeek(out var time) && time < cutoff)
                {
                    _requestTimes.TryDequeue(out _);
                }

                if (_requestTimes.Count < limit)
                {
                    _requestTimes.Enqueue(now);
                    return true;
                }

                return false;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private TimeSpan CalculateRetryAfter()
        {
            if (_requestTimes.TryPeek(out var oldest))
            {
                var nextAvailable = oldest + period;
                return nextAvailable - DateTime.UtcNow;
            }

            return TimeSpan.Zero;
        }

        public void Reset()
        {
            _semaphore.Wait();
            try
            {
                _requestTimes.Clear();
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}