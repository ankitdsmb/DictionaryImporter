namespace DictionaryImporter.Core.Orchestration.Concurrency;

public sealed class ImportConcurrencyManager
{
    private readonly ILogger<ImportConcurrencyManager> _logger;
    private readonly ParallelProcessingSettings _settings;

    private readonly SemaphoreSlim _dbSemaphore;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sourceSemaphores = new();
    private readonly ConcurrentDictionary<string, DateTime> _sourceStartTimes = new();
    private readonly ConcurrentDictionary<string, long> _sourceDurations = new();
    private readonly ConcurrentDictionary<string, int> _activeSources = new();

    public ImportConcurrencyManager(
        ILogger<ImportConcurrencyManager> logger,
        IOptions<ParallelProcessingSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? ParallelProcessingSettings.Default;

        _dbSemaphore = new SemaphoreSlim(
            _settings.MaxDatabaseConnections,
            _settings.MaxDatabaseConnections);
    }

    public async Task<TResult> ExecuteWithConcurrencyControl<TResult>(
        string sourceCode,
        Func<Task<TResult>> operation,
        CancellationToken ct)
    {
        var startTime = DateTime.UtcNow;
        var sourceSemaphore =
            _sourceSemaphores.GetOrAdd(sourceCode, _ => new SemaphoreSlim(1, 1));

        await _dbSemaphore.WaitAsync(ct);
        try
        {
            await sourceSemaphore.WaitAsync(ct);
            try
            {
                _activeSources.AddOrUpdate(sourceCode, 1, (_, _) => 1);
                _sourceStartTimes[sourceCode] = startTime;

                _logger.LogInformation(
                    "Concurrent operation started | Source={SourceCode} | ActiveSources={Active}/{Max} | DbInUse={DbUsed}",
                    sourceCode,
                    _activeSources.Count,
                    _settings.DegreeOfParallelism,
                    _settings.MaxDatabaseConnections - _dbSemaphore.CurrentCount);

                return await operation();
            }
            finally
            {
                sourceSemaphore.Release();

                var duration =
                    (long)(DateTime.UtcNow - startTime).TotalMilliseconds;

                _sourceDurations[sourceCode] = duration;
                _activeSources.TryRemove(sourceCode, out _);
            }
        }
        finally
        {
            _dbSemaphore.Release();

            _logger.LogDebug(
                "Concurrent operation completed | Source={SourceCode} | Duration={Duration}ms | DbAvailable={Available}",
                sourceCode,
                _sourceDurations.GetValueOrDefault(sourceCode, 0),
                _dbSemaphore.CurrentCount);
        }
    }

    public async Task ExecuteWithConcurrencyControl(
        string sourceCode,
        Func<Task> operation,
        CancellationToken ct)
    {
        await ExecuteWithConcurrencyControl(
            sourceCode,
            async () =>
            {
                await operation();
                return true;
            },
            ct);
    }

    public ParallelOptions GetParallelOptions(CancellationToken ct)
    {
        return new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = _settings.EnableParallelProcessing
                ? _settings.DegreeOfParallelism
                : 1
        };
    }

    public ParallelOptions GetParallelOptionsForSource(
        string sourceCode,
        CancellationToken ct)
    {
        // Source-level parallelism is intentionally serialized
        return GetParallelOptions(ct);
    }

    public int GetOptimalBatchSize(string sourceCode)
    {
        // Batch size should be stable; source serialization already protects DB
        return _settings.BatchSize;
    }

    public ConcurrencyMetrics GetMetrics()
    {
        return new ConcurrencyMetrics
        {
            AvailableSlots = _dbSemaphore.CurrentCount,
            MaxConcurrency = _settings.MaxDatabaseConnections,
            ActiveSources = _activeSources.Count,
            SourceConcurrency = _activeSources.ToDictionary(),
            SourceDurations = _sourceDurations.ToDictionary(),
            QueueLength =
                _settings.MaxDatabaseConnections - _dbSemaphore.CurrentCount,
            Settings = new ParallelProcessingSettings
            {
                DegreeOfParallelism = _settings.DegreeOfParallelism,
                MaxConcurrentBatches = _settings.MaxConcurrentBatches,
                BatchSize = _settings.BatchSize,
                MaxDatabaseConnections = _settings.MaxDatabaseConnections,
                EnableParallelProcessing = _settings.EnableParallelProcessing,
                RetryCount = _settings.RetryCount,
                RetryDelayMs = _settings.RetryDelayMs
            }
        };
    }
}