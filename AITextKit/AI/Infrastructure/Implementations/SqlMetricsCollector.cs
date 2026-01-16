namespace DictionaryImporter.AITextKit.AI.Infrastructure.Implementations;

public class SqlMetricsCollector : IPerformanceMetricsCollector
{
    private readonly string _connectionString;
    private readonly ILogger<SqlMetricsCollector> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Timer _aggregationTimer;
    private readonly object _batchLock = new();
    private readonly List<ProviderMetrics> _batchBuffer = new();

    public SqlMetricsCollector(
        IOptions<DatabaseOptions> dbOptions,
        ILogger<SqlMetricsCollector> logger)
    {
        _connectionString = dbOptions.Value.ConnectionString;
        _logger = logger;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        _aggregationTimer = new Timer(
            _ => AggregateMetricsAsync().GetAwaiter().GetResult(),
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));
    }

    public async Task RecordMetricsAsync(ProviderMetrics metrics)
    {
        try
        {
            lock (_batchLock)
            {
                _batchBuffer.Add(metrics);
            }

            if (_batchBuffer.Count >= 100)
            {
                await ProcessBatchAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record metrics for provider {Provider}", metrics.ProviderName);
        }
    }

    private async Task ProcessBatchAsync()
    {
        List<ProviderMetrics> batchToProcess;
        lock (_batchLock)
        {
            batchToProcess = new List<ProviderMetrics>(_batchBuffer);
            _batchBuffer.Clear();
        }

        if (batchToProcess.Count == 0)
            return;

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();

            try
            {
                foreach (var metrics in batchToProcess)
                {
                    await UpdateProviderMetricsAsync(connection, transaction, metrics);
                }

                await transaction.CommitAsync();
                _logger.LogDebug("Processed {Count} metrics records", batchToProcess.Count);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process metrics batch");

            lock (_batchLock)
            {
                _batchBuffer.InsertRange(0, batchToProcess);
            }
        }
    }

    private async Task UpdateProviderMetricsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        ProviderMetrics metrics)
    {
        var sql = @"
            MERGE ProviderPerformanceMetrics AS target
            USING (SELECT @ProviderName as ProviderName,
                          @MetricDate as MetricDate) AS source
            ON target.ProviderName = source.ProviderName
                AND target.MetricDate = source.MetricDate
            WHEN MATCHED THEN
                UPDATE SET
                    TotalRequests = TotalRequests + @TotalRequests,
                    SuccessfulRequests = SuccessfulRequests + @SuccessfulRequests,
                    FailedRequests = FailedRequests + @FailedRequests,
                    TotalTokens = TotalTokens + @TotalTokens,
                    TotalCost = TotalCost + @TotalCost,
                    TotalDurationMs = TotalDurationMs + @TotalDurationMs,
                    UpdatedAt = GETUTCDATE(),
                    ErrorBreakdown = ISNULL(JSON_MODIFY(
                        ISNULL(ErrorBreakdown, '{}'),
                        '$.errors.' + @ErrorType,
                        ISNULL(JSON_VALUE(ErrorBreakdown, '$.errors.' + @ErrorType), 0) + 1
                    ), '{}')
            WHEN NOT MATCHED THEN
                INSERT (ProviderName, MetricDate, TotalRequests, SuccessfulRequests,
                        FailedRequests, TotalTokens, TotalCost, TotalDurationMs,
                        ErrorBreakdown)
                VALUES (@ProviderName, @MetricDate, @TotalRequests, @SuccessfulRequests,
                        @FailedRequests, @TotalTokens, @TotalCost, @TotalDurationMs,
                        CASE WHEN @ErrorType IS NOT NULL
                            THEN JSON_OBJECT('errors': JSON_OBJECT(@ErrorType: 1))
                            ELSE '{}' END);
        ";

        string primaryErrorType = null;
        if (metrics.ErrorBreakdown != null && metrics.ErrorBreakdown.Any())
        {
            primaryErrorType = metrics.ErrorBreakdown.First().Key;
        }

        var parameters = new
        {
            metrics.ProviderName,
            metrics.MetricDate,
            metrics.TotalRequests,
            metrics.SuccessfulRequests,
            metrics.FailedRequests,
            metrics.TotalTokens,
            metrics.TotalCost,
            metrics.TotalDurationMs,
            ErrorType = primaryErrorType
        };

        await connection.ExecuteAsync(sql, parameters, transaction);
    }

    public async Task<ProviderPerformance> GetProviderPerformanceAsync(
        string providerName,
        DateTime from,
        DateTime to)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);

            var sql = @"
                SELECT
                    ProviderName,
                    MetricDate,
                    TotalRequests,
                    SuccessfulRequests,
                    FailedRequests,
                    TotalTokens,
                    TotalCost,
                    TotalDurationMs,
                    CASE WHEN TotalRequests > 0
                        THEN CAST(SuccessfulRequests AS FLOAT) / TotalRequests * 100
                        ELSE 0 END as SuccessRate,
                    CASE WHEN SuccessfulRequests > 0
                        THEN CAST(TotalDurationMs AS FLOAT) / SuccessfulRequests
                        ELSE 0 END as AvgResponseTimeMs
                FROM ProviderPerformanceMetrics
                WHERE ProviderName = @ProviderName
                    AND MetricDate >= @From
                    AND MetricDate <= @To
                ORDER BY MetricDate;
            ";

            var records = await connection.QueryAsync<ProviderPerformanceRecord>(
                sql,
                new { ProviderName = providerName, From = from.Date, To = to.Date });

            var performance = new ProviderPerformance
            {
                Provider = providerName,
                PeriodFrom = from,
                PeriodTo = to
            };

            foreach (var record in records)
            {
                performance.TotalRequests += record.TotalRequests;
                performance.SuccessfulRequests += record.SuccessfulRequests;
                performance.FailedRequests += record.FailedRequests;
                performance.TotalTokens += record.TotalTokens;
                performance.TotalCost += record.TotalCost;
                performance.TotalProcessingTime += TimeSpan.FromMilliseconds(record.TotalDurationMs);
            }

            if (performance.SuccessfulRequests > 0)
            {
                performance.AverageResponseTimeMs = performance.TotalProcessingTime.TotalMilliseconds / performance.SuccessfulRequests;
            }

            return performance;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get provider performance for {Provider}", providerName);
            return new ProviderPerformance { Provider = providerName };
        }
    }

    public async Task<IEnumerable<ProviderPerformance>> GetAllProvidersPerformanceAsync(
        DateTime from,
        DateTime to)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);

            var sql = @"
                SELECT
                    ProviderName,
                    SUM(TotalRequests) as TotalRequests,
                    SUM(SuccessfulRequests) as SuccessfulRequests,
                    SUM(FailedRequests) as FailedRequests,
                    SUM(TotalTokens) as TotalTokens,
                    SUM(TotalCost) as TotalCost,
                    SUM(TotalDurationMs) as TotalDurationMs
                FROM ProviderPerformanceMetrics
                WHERE MetricDate >= @From
                    AND MetricDate <= @To
                GROUP BY ProviderName
                ORDER BY TotalRequests DESC;
            ";

            var records = await connection.QueryAsync<ProviderPerformanceSummary>(
                sql,
                new { From = from.Date, To = to.Date });

            var performances = new List<ProviderPerformance>();

            foreach (var record in records)
            {
                var performance = new ProviderPerformance
                {
                    Provider = record.ProviderName,
                    TotalRequests = record.TotalRequests,
                    SuccessfulRequests = record.SuccessfulRequests,
                    FailedRequests = record.FailedRequests,
                    TotalTokens = record.TotalTokens,
                    TotalCost = record.TotalCost,
                    PeriodFrom = from,
                    PeriodTo = to
                };

                if (record.SuccessfulRequests > 0)
                {
                    performance.AverageResponseTimeMs = (double)record.TotalDurationMs / record.SuccessfulRequests;
                    performance.TotalProcessingTime = TimeSpan.FromMilliseconds(record.TotalDurationMs);
                }

                performances.Add(performance);
            }

            return performances;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all providers performance");
            return Enumerable.Empty<ProviderPerformance>();
        }
    }

    private async Task AggregateMetricsAsync()
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);

            var sql = @"
                -- Calculate percentiles from audit logs for the last hour
                WITH ResponseTimes AS (
                    SELECT
                        ProviderName,
                        DurationMs,
                        PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY DurationMs)
                            OVER (PARTITION BY ProviderName) as P95,
                        PERCENTILE_CONT(0.99) WITHIN GROUP (ORDER BY DurationMs)
                            OVER (PARTITION BY ProviderName) as P99
                    FROM RequestAuditLogs
                    WHERE CreatedAt >= DATEADD(HOUR, -1, GETUTCDATE())
                        AND Success = 1
                )
                UPDATE ppm
                SET
                    P95ResponseTimeMs = rt.P95,
                    P99ResponseTimeMs = rt.P99,
                    UpdatedAt = GETUTCDATE()
                FROM ProviderPerformanceMetrics ppm
                INNER JOIN (
                    SELECT DISTINCT
                        ProviderName,
                        P95,
                        P99
                    FROM ResponseTimes
                ) rt ON ppm.ProviderName = rt.ProviderName
                WHERE ppm.MetricDate = CAST(GETUTCDATE() AS DATE);
            ";

            var affected = await connection.ExecuteAsync(sql);

            if (affected > 0)
            {
                _logger.LogDebug("Aggregated metrics for {Count} providers", affected);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to aggregate metrics");
        }
    }

    public void Dispose()
    {
        _aggregationTimer?.Dispose();

        if (_batchBuffer.Count > 0)
        {
            ProcessBatchAsync().GetAwaiter().GetResult();
        }
    }
}