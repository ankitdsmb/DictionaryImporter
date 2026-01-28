namespace DictionaryImporter.Orchestration;

public sealed class ImportConcurrencyManager
{
    private readonly ILogger<ImportConcurrencyManager> _logger;
    private readonly ParallelProcessingSettings _settings;

    private readonly SemaphoreSlim _dbSemaphore;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sourceSemaphores = new();
    private readonly ConcurrentDictionary<string, DateTime> _sourceStartTimes = new();
    private readonly ConcurrentDictionary<string, long> _sourceDurations = new();
    private readonly ConcurrentDictionary<string, int> _sourceConcurrencyCount = new();

    public ImportConcurrencyManager(
        ILogger<ImportConcurrencyManager> logger,
        IOptions<ParallelProcessingSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? ParallelProcessingSettings.Default;

        // Initialize _dbSemaphore here, not in field initializer
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
        var sourceSemaphore = _sourceSemaphores.GetOrAdd(sourceCode, _ => new SemaphoreSlim(1, 1));

        await _dbSemaphore.WaitAsync(ct);
        try
        {
            // Track concurrency for this source
            _sourceConcurrencyCount.AddOrUpdate(sourceCode, 1, (_, count) => count + 1);

            // Allow only one operation per source at a time
            await sourceSemaphore.WaitAsync(ct);
            try
            {
                _sourceStartTimes[sourceCode] = startTime;
                _logger.LogInformation(
                    "Concurrent operation started | Source={SourceCode} | ActiveSources={ActiveSources}/{Max} | ConcurrentForSource={ConcurrentCount}",
                    sourceCode,
                    _settings.MaxDatabaseConnections - _dbSemaphore.CurrentCount,
                    _settings.MaxDatabaseConnections,
                    _sourceConcurrencyCount[sourceCode]);

                var result = await operation();
                return result;
            }
            finally
            {
                sourceSemaphore.Release();
                var duration = (long)(DateTime.UtcNow - startTime).TotalMilliseconds;
                _sourceDurations[sourceCode] = duration;
                _sourceConcurrencyCount.AddOrUpdate(sourceCode, 0, (_, count) => Math.Max(0, count - 1));
            }
        }
        finally
        {
            _dbSemaphore.Release();
            _logger.LogDebug(
                "Concurrent operation completed | Source={SourceCode} | Duration={Duration}ms | AvailableSlots={AvailableSlots}",
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
        await ExecuteWithConcurrencyControl<bool>(
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
            MaxDegreeOfParallelism = _settings.DegreeOfParallelism
        };
    }

    public ParallelOptions GetParallelOptionsForSource(string sourceCode, CancellationToken ct)
    {
        // Allow more parallelism for sources that are not running concurrently
        var baseParallelism = _settings.DegreeOfParallelism;
        var currentConcurrency = _sourceConcurrencyCount.GetValueOrDefault(sourceCode, 1);

        // Adjust parallelism based on current concurrency
        var adjustedParallelism = currentConcurrency > 1
            ? Math.Max(1, baseParallelism / currentConcurrency)
            : baseParallelism;

        return new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = adjustedParallelism
        };
    }

    public int GetOptimalBatchSize(string sourceCode)
    {
        // Adjust batch size based on source concurrency
        var currentConcurrency = _sourceConcurrencyCount.GetValueOrDefault(sourceCode, 1);
        var baseBatchSize = _settings.BatchSize;

        return currentConcurrency > 1
            ? Math.Max(100, baseBatchSize / currentConcurrency)
            : baseBatchSize;
    }

    public ConcurrencyMetrics GetMetrics()
    {
        var activeSources = _sourceConcurrencyCount
            .Where(kv => kv.Value > 0)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        return new ConcurrencyMetrics
        {
            AvailableSlots = _dbSemaphore.CurrentCount,
            MaxConcurrency = _settings.MaxDatabaseConnections,
            ActiveSources = activeSources.Count,
            SourceConcurrency = activeSources,
            SourceDurations = _sourceDurations.ToDictionary(),
            QueueLength = _settings.MaxDatabaseConnections - _dbSemaphore.CurrentCount,
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