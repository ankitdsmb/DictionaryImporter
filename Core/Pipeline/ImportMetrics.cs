using System.Diagnostics;

namespace DictionaryImporter.Core.Pipeline
{
    public sealed class ImportMetrics
    {
        public long RawEntriesExtracted { get; private set; }
        public long EntriesTransformed { get; private set; }
        public long EntriesStaged { get; private set; }
        public TimeSpan Duration { get; private set; }

        private readonly Stopwatch _stopwatch = new();

        public void Start() => _stopwatch.Start();

        public void Stop()
        {
            _stopwatch.Stop();
            Duration = _stopwatch.Elapsed;
        }

        public void IncrementRaw() => RawEntriesExtracted++;

        public void AddTransformed(int count)
        {
            EntriesTransformed += count;
            EntriesStaged += count;
        }
        public long EntriesRejected { get; private set; }

        public void IncrementRejected() => EntriesRejected++;
    }
}
