using System.Diagnostics;

namespace DictionaryImporter.Core.Pipeline;

public sealed class ImportMetrics
{
    private readonly Stopwatch _stopwatch = new();
    public int RawEntriesExtracted { get; private set; }
    public int EntriesStaged { get; private set; }
    public int EntriesRejected { get; private set; }

    // ================= NEW =================
    public int RejectedByCanonicalEligibility { get; private set; }

    public int RejectedByValidator { get; private set; }
    // ======================================

    public TimeSpan Duration => _stopwatch.Elapsed;

    public void Start()
    {
        _stopwatch.Start();
    }

    public void Stop()
    {
        _stopwatch.Stop();
    }

    public void IncrementRaw()
    {
        RawEntriesExtracted++;
    }

    public void AddTransformed(int count)
    {
        EntriesStaged += count;
    }

    public void IncrementCanonicalEligibilityRejected()
    {
        EntriesRejected++;
        RejectedByCanonicalEligibility++;
    }

    public void IncrementValidatorRejected()
    {
        EntriesRejected++;
        RejectedByValidator++;
    }
}