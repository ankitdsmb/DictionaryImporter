using System.Collections;
using System.Collections.Concurrent;
using System.Data;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Persistence.Batched
{
    /// <summary>
    /// Generic SQL operation batcher that automatically groups similar operations
    /// to reduce database round-trips, with SQL Server parameter limit protection.
    /// </summary>
    public sealed class GenericSqlBatcher : IAsyncDisposable, IDisposable
    {
        private readonly string _connectionString;
        private readonly ILogger<GenericSqlBatcher> _logger;

        private readonly ConcurrentDictionary<string, BatchedOperation> _operations = new();
        private readonly ConcurrentDictionary<string, int> _operationCounts = new();

        private readonly ConcurrentDictionary<string, int> _sqlParameterCountCache = new(StringComparer.Ordinal);

        private readonly Timer _flushTimer;
        private readonly SemaphoreSlim _flushSemaphore = new(1, 1);

        private volatile bool _disposed;

        // NOTE: SQL Server hard limit = 2100 parameters. Keep buffer.
        private const int MaxParametersPerQuery = 2000;

        private const int DefaultMaxBatchSize = 100; // safe global cap
        private const int MaxBatchWaitMs = 2000;      // flush window
        private const int MaxRetries = 3;

        private int _totalOperationsQueued;
        private int _totalBatchesExecuted;

        private static readonly Regex ParamRegex =
            new(@"@[a-zA-Z_][a-zA-Z0-9_]*", RegexOptions.Compiled);

        public GenericSqlBatcher(string connectionString, ILogger<GenericSqlBatcher> logger)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _flushTimer = new Timer(
                static state => ((GenericSqlBatcher)state!).FlushDueBatchesSafe(),
                this,
                MaxBatchWaitMs,
                MaxBatchWaitMs);

            _logger.LogInformation(
                "GenericSqlBatcher initialized with {BatchSize} max batch size",
                DefaultMaxBatchSize);
        }

        /// <summary>
        /// Queue a SQL operation for batch execution with dynamic batch size limiting.
        /// </summary>
        public async Task QueueOperationAsync(
            string operationKey,
            string sql,
            object parameters,
            CommandType commandType = CommandType.Text,
            int timeoutSeconds = 30,
            CancellationToken ct = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GenericSqlBatcher));
            if (string.IsNullOrWhiteSpace(operationKey)) throw new ArgumentException("Operation key is required", nameof(operationKey));
            if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("SQL is required", nameof(sql));
            if (parameters is null) throw new ArgumentNullException(nameof(parameters));

            var totalQueued = Interlocked.Increment(ref _totalOperationsQueued);
            _operationCounts.AddOrUpdate(operationKey, 1, static (_, count) => count + 1);

            _logger.LogTrace(
                "Operation queued | Key={Key} | TotalQueued={Total} | KeyCount={KeyCount}",
                operationKey, totalQueued, _operationCounts[operationKey]);

            var parametersPerOp = EstimateParameterCountCached(sql);
            var maxSafeBatchSize = CalculateMaxSafeBatchSize(parametersPerOp);

            var batch = _operations.GetOrAdd(
                operationKey,
                key => new BatchedOperation(
                    operationKey: key,
                    sqlTemplate: sql,
                    commandType: commandType,
                    timeoutSeconds: timeoutSeconds,
                    maxBatchSize: maxSafeBatchSize,
                    logger: _logger));

            batch.AddParameters(parameters);

            // batch full -> flush
            if (batch.Count >= batch.MaxBatchSize)
            {
                _logger.LogInformation(
                    "Batch full | Key={Key} | Size={Size}/{MaxSize} | ParamsPerOp={ParamsPerOp} | Triggering flush",
                    operationKey, batch.Count, batch.MaxBatchSize, batch.ParametersPerOperation);

                await FlushBatchAsync(operationKey, ct);
            }
        }

        /// <summary>
        /// Execute an operation immediately (bypass batching).
        /// </summary>
        public async Task<int> ExecuteImmediateAsync(
            string sql,
            object parameters,
            CommandType commandType = CommandType.Text,
            int timeoutSeconds = 30,
            CancellationToken ct = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GenericSqlBatcher));
            if (string.IsNullOrWhiteSpace(sql)) throw new ArgumentException("SQL is required", nameof(sql));
            if (parameters is null) throw new ArgumentNullException(nameof(parameters));

            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);

                return await connection.ExecuteAsync(
                    new CommandDefinition(
                        sql,
                        parameters,
                        commandTimeout: timeoutSeconds,
                        commandType: commandType,
                        cancellationToken: ct));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Immediate execution failed for SQL: {Sql}", sql);
                throw;
            }
        }

        /// <summary>
        /// Force flush all pending operations.
        /// </summary>
        public async Task FlushAllAsync(CancellationToken ct = default)
        {
            if (_disposed) return;

            var keys = _operations.Keys.ToList();
            _logger.LogDebug("Flushing all {Count} pending batches", keys.Count);

            foreach (var key in keys)
            {
                await FlushBatchAsync(key, ct);
            }
        }

        private async Task FlushBatchAsync(string operationKey, CancellationToken ct)
        {
            if (_disposed) return;

            // Prevent concurrent flush collisions/timer + manual flush overlaps
            await _flushSemaphore.WaitAsync(ct);
            try
            {
                if (!_operations.TryRemove(operationKey, out var batch))
                    return;

                if (batch.Count == 0)
                    return;

                var totalParams = batch.Count * batch.ParametersPerOperation;

                if (totalParams > MaxParametersPerQuery)
                {
                    _logger.LogWarning(
                        "Batch {OperationKey} has {TotalParams} parameters, exceeding limit. Will chunk.",
                        operationKey, totalParams);

                    await ExecuteBatchInChunksAsync(batch, ct);
                    return;
                }

                await ExecuteBatchAsync(batch, ct);
            }
            finally
            {
                _flushSemaphore.Release();
            }
        }

        private async Task ExecuteBatchInChunksAsync(BatchedOperation originalBatch, CancellationToken ct)
        {
            var chunkSize = CalculateMaxSafeBatchSize(originalBatch.ParametersPerOperation);
            var totalOps = originalBatch.Count;

            if (chunkSize <= 0) chunkSize = 1;

            var totalChunks = (int)Math.Ceiling(totalOps / (double)chunkSize);

            _logger.LogInformation(
                "Chunking batch {OperationKey} | TotalOps={TotalOps} | ChunkSize={ChunkSize} | Chunks={TotalChunks}",
                originalBatch.OperationKey, totalOps, chunkSize, totalChunks);

            // Copy once (avoid repeated enumeration + preserve snapshot)
            var allParameters = originalBatch.GetAllParametersSnapshot();

            for (var chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
            {
                var startIndex = chunkIndex * chunkSize;
                var endIndex = Math.Min(startIndex + chunkSize, totalOps);
                var chunkCount = endIndex - startIndex;

                if (chunkCount <= 0) continue;

                var chunkBatch = new BatchedOperation(
                    operationKey: originalBatch.OperationKey,
                    sqlTemplate: originalBatch.SqlTemplate,
                    commandType: originalBatch.CommandType,
                    timeoutSeconds: originalBatch.TimeoutSeconds,
                    maxBatchSize: chunkSize,
                    logger: _logger);

                for (var i = startIndex; i < endIndex; i++)
                    chunkBatch.AddParameterDictionary(allParameters[i]);

                _logger.LogDebug(
                    "Executing chunk {Chunk}/{TotalChunks} | Size={ChunkCount}",
                    chunkIndex + 1, totalChunks, chunkCount);

                await ExecuteBatchAsync(chunkBatch, ct);
            }
        }

        private async Task ExecuteBatchAsync(BatchedOperation batch, CancellationToken ct)
        {
            var totalBatches = Interlocked.Increment(ref _totalBatchesExecuted);

            _logger.LogInformation(
                "EXECUTING BATCH | Key={Key} | Operations={Count} | TotalBatches={TotalBatches} | TotalQueued={TotalQueued} | ParamsPerOp={ParamsPerOp}",
                batch.OperationKey, batch.Count, totalBatches, Volatile.Read(ref _totalOperationsQueued), batch.ParametersPerOperation);

            var retryCount = 0;

            while (true)
            {
                try
                {
                    await using var connection = new SqlConnection(_connectionString);
                    await connection.OpenAsync(ct);

                    var batchSql = batch.GenerateBatchSql();
                    var batchParameters = batch.GetBatchParameters();

                    var totalParams = batch.Count * batch.ParametersPerOperation;

                    _logger.LogDebug(
                        "Batch SQL parameters: {ParamCount} total ({Ops} ops × {ParamsPerOp} params each)",
                        totalParams, batch.Count, batch.ParametersPerOperation);

                    if (totalParams > MaxParametersPerQuery)
                    {
                        _logger.LogError(
                            "CRITICAL: Batch {OperationKey} would exceed parameter limit: {TotalParams} > {MaxParams}",
                            batch.OperationKey, totalParams, MaxParametersPerQuery);

                        throw new InvalidOperationException(
                            $"Batch would exceed SQL Server parameter limit: {totalParams} > {MaxParametersPerQuery}");
                    }

                    var rows = await connection.ExecuteAsync(
                        new CommandDefinition(
                            batchSql,
                            batchParameters,
                            commandTimeout: batch.TimeoutSeconds,
                            commandType: batch.CommandType,
                            cancellationToken: ct));

                    _logger.LogInformation(
                        "Batch {OperationKey} executed successfully | Operations={Count} | RowsAffected={Rows}",
                        batch.OperationKey, batch.Count, rows);

                    return;
                }
                catch (SqlException sqlEx) when (sqlEx.Number == 1205) // Deadlock
                {
                    retryCount++;

                    if (retryCount > MaxRetries)
                    {
                        _logger.LogError(
                            sqlEx, "Batch {OperationKey} failed after {MaxRetries} retries",
                            batch.OperationKey, MaxRetries);
                        throw;
                    }

                    _logger.LogWarning(
                        sqlEx, "Deadlock detected on batch {OperationKey}, retry {Retry}/{MaxRetries}",
                        batch.OperationKey, retryCount, MaxRetries);

                    await Task.Delay(100 * retryCount, ct);
                }
                catch (SqlException sqlEx) when (sqlEx.Number == 8003) // Param overflow (rare)
                {
                    _logger.LogError(
                        sqlEx, "Parameter overflow in batch {OperationKey}. Will retry with smaller chunks.",
                        batch.OperationKey);

                    await ExecuteBatchInChunksAsync(batch, ct);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex, "Batch {OperationKey} failed with {Count} operations",
                        batch.OperationKey, batch.Count);

                    await SaveFailedBatchForRecovery(batch, ex, ct);
                    throw;
                }
            }
        }

        private void FlushDueBatchesSafe()
        {
            _ = Task.Run(async () =>
            {
                if (_disposed) return;

                try
                {
                    var now = DateTime.UtcNow;

                    var dueKeys = _operations
                        .Where(kvp => kvp.Value.CreatedAt.AddMilliseconds(MaxBatchWaitMs) <= now)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var key in dueKeys)
                        await FlushBatchAsync(key, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in batch flush timer");
                }
            });
        }

        private async Task SaveFailedBatchForRecovery(BatchedOperation batch, Exception ex, CancellationToken ct)
        {
            try
            {
                const string recoveryTableSql = @"
IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'BatchRecovery')
BEGIN
    CREATE TABLE dbo.BatchRecovery (
        RecoveryId UNIQUEIDENTIFIER DEFAULT NEWID() PRIMARY KEY,
        OperationKey NVARCHAR(200) NOT NULL,
        BatchSql NVARCHAR(MAX) NOT NULL,
        ParametersJson NVARCHAR(MAX) NULL,
        ErrorMessage NVARCHAR(MAX) NULL,
        OperationCount INT NOT NULL,
        CreatedUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
END";

                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync(ct);

                await connection.ExecuteAsync(new CommandDefinition(recoveryTableSql, cancellationToken: ct));

                const string insertSql = @"
INSERT INTO dbo.BatchRecovery
(OperationKey, BatchSql, ParametersJson, ErrorMessage, OperationCount)
VALUES (@OperationKey, @BatchSql, @ParametersJson, @ErrorMessage, @OperationCount)";

                await connection.ExecuteAsync(
                    new CommandDefinition(
                        insertSql,
                        new
                        {
                            batch.OperationKey,
                            BatchSql = batch.SqlTemplate,
                            ParametersJson = JsonSerializer.Serialize(batch.GetAllParametersSnapshot()),
                            ErrorMessage = ex.ToString(),
                            OperationCount = batch.Count
                        },
                        cancellationToken: ct));

                _logger.LogWarning(
                    "Saved failed batch {OperationKey} with {Count} operations to recovery table",
                    batch.OperationKey, batch.Count);
            }
            catch (Exception recoveryEx)
            {
                _logger.LogError(recoveryEx, "Failed to save batch for recovery");
            }
        }

        private int EstimateParameterCountCached(string sql)
        {
            if (_sqlParameterCountCache.TryGetValue(sql, out var cached))
                return cached;

            var matches = ParamRegex.Matches(sql);

            var count = matches
                .Select(m => m.Value)
                .Where(static p => !p.StartsWith("@@", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Count();

            _sqlParameterCountCache[sql] = count;
            return count;
        }

        private static int CalculateMaxSafeBatchSize(int parametersPerOperation)
        {
            if (parametersPerOperation <= 0)
                return DefaultMaxBatchSize;

            var maxByParams = (MaxParametersPerQuery - 100) / parametersPerOperation;
            return Math.Max(1, Math.Min(maxByParams, DefaultMaxBatchSize));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _flushTimer.Dispose();
            _flushSemaphore.Dispose();

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                FlushAllAsync(cts.Token).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during final batch flush on dispose");
            }

            _logger.LogInformation("GenericSqlBatcher disposed");
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            _flushTimer.Dispose();

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await FlushAllAsync(cts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during final batch flush on dispose async");
            }
            finally
            {
                _flushSemaphore.Dispose();
            }

            _logger.LogInformation("GenericSqlBatcher disposed");
        }

        // ============================================================
        // Inner class: BatchedOperation
        // ============================================================

        private sealed class BatchedOperation
        {
            public string OperationKey { get; }
            public string SqlTemplate { get; }
            public CommandType CommandType { get; }
            public int TimeoutSeconds { get; }
            public int MaxBatchSize { get; }
            public int ParametersPerOperation { get; }
            public int Count => _parameterBatches.Count;
            public DateTime CreatedAt { get; }

            private readonly List<Dictionary<string, object?>> _parameterBatches = new();
            private readonly ILogger _logger;

            private readonly string[] _paramNames; // cached once per batch template

            public BatchedOperation(
                string operationKey,
                string sqlTemplate,
                CommandType commandType,
                int timeoutSeconds,
                int maxBatchSize,
                ILogger logger)
            {
                OperationKey = operationKey;
                SqlTemplate = sqlTemplate;
                CommandType = commandType;
                TimeoutSeconds = timeoutSeconds;
                MaxBatchSize = maxBatchSize;
                CreatedAt = DateTime.UtcNow;
                _logger = logger;

                _paramNames = ExtractParamNames(sqlTemplate);
                ParametersPerOperation = _paramNames.Length;
            }

            public void AddParameters(object parameters)
            {
                _parameterBatches.Add(ConvertToDictionary(parameters));

                _logger.LogTrace(
                    "Added parameters to batch {OperationKey}, total: {Count}/{MaxBatchSize}",
                    OperationKey, _parameterBatches.Count, MaxBatchSize);
            }

            public void AddParameterDictionary(Dictionary<string, object?> paramDict)
            {
                _parameterBatches.Add(paramDict);
            }

            public string GenerateBatchSql()
            {
                if (_parameterBatches.Count == 0)
                    return string.Empty;

                var endsWithSemicolon = SqlTemplate.TrimEnd().EndsWith(';');

                var sb = new StringBuilder(capacity: SqlTemplate.Length * _parameterBatches.Count);

                for (var i = 0; i < _parameterBatches.Count; i++)
                {
                    if (i > 0) sb.AppendLine();

                    sb.Append(ParameterizeSql(SqlTemplate, i));

                    if (!endsWithSemicolon)
                        sb.Append(';');
                }

                return sb.ToString();
            }

            public object GetBatchParameters()
            {
                var dp = new DynamicParameters();

                for (var batchIndex = 0; batchIndex < _parameterBatches.Count; batchIndex++)
                {
                    var paramDict = _parameterBatches[batchIndex];

                    foreach (var kvp in paramDict)
                    {
                        var paramName = $"p{batchIndex}_{kvp.Key}";
                        dp.Add(paramName, kvp.Value);
                    }
                }

                return dp;
            }

            public List<Dictionary<string, object?>> GetAllParametersSnapshot()
                => new(_parameterBatches);

            private string ParameterizeSql(string sql, int batchIndex)
            {
                // IMPORTANT:
                // Use regex replacement to avoid wrong replacements like:
                // @Id inside @Id2
                // and avoid replacing @@VERSION etc.
                return Regex.Replace(
                    sql,
                    @"@([a-zA-Z_][a-zA-Z0-9_]*)",
                    m =>
                    {
                        var name = m.Groups[1].Value;
                        if (name.StartsWith("@", StringComparison.Ordinal)) return m.Value; // safety (should never happen)
                        if (m.Value.StartsWith("@@", StringComparison.Ordinal)) return m.Value;

                        // Only rewrite known parameters
                        // (this prevents rewriting accidental tokens)
                        if (_paramNames.Contains(name, StringComparer.Ordinal))
                            return $"@p{batchIndex}_{name}";

                        return m.Value;
                    },
                    RegexOptions.Compiled);
            }

            private static string[] ExtractParamNames(string sql)
            {
                var matches = ParamRegex.Matches(sql);

                return matches
                    .Select(m => m.Value)
                    .Where(static p => !p.StartsWith("@@", StringComparison.Ordinal))
                    .Select(static p => p[1..]) // remove '@'
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            }

            private static Dictionary<string, object?> ConvertToDictionary(object parameters)
            {
                if (parameters is Dictionary<string, object?> dict)
                    return dict;

                if (parameters is IDictionary idict)
                {
                    var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (System.Collections.DictionaryEntry entry in idict)
                    {
                        var key = entry.Key?.ToString();
                        if (string.IsNullOrWhiteSpace(key))
                            continue;

                        result[key] = entry.Value;
                    }
                    return result;
                }

                var props = parameters.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

                var propertyDict = new Dictionary<string, object?>(props.Length, StringComparer.Ordinal);

                foreach (var prop in props)
                    propertyDict[prop.Name] = prop.GetValue(parameters);

                return propertyDict;
            }
        }
    }
}
