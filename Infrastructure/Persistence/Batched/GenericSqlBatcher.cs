using System.Collections;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Data;
using System.Reflection;
using System.Text;
using DictionaryEntry = DictionaryImporter.Domain.Models.DictionaryEntry;

namespace DictionaryImporter.Infrastructure.Persistence.Batched
{
    /// <summary>
    /// Generic SQL operation batcher that automatically groups similar operations
    /// to reduce database round-trips
    /// </summary>
    public sealed class GenericSqlBatcher : IDisposable
    {
        private readonly string _connectionString;
        private readonly ILogger<GenericSqlBatcher> _logger;
        private readonly ConcurrentDictionary<string, BatchedOperation> _operations = new();
        private readonly Timer _flushTimer;
        private readonly object _flushLock = new();
        private bool _disposed;

        // Configuration
        private const int MaxBatchSize = 1000;

        private const int MaxBatchWaitMs = 2000; // 2 seconds
        private const int MaxRetries = 3;
        private int _totalOperationsQueued = 0;
        private int _totalBatchesExecuted = 0;
        private readonly ConcurrentDictionary<string, int> _operationCounts = new();

        public GenericSqlBatcher(string connectionString, ILogger<GenericSqlBatcher> logger)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Start timer for periodic flushing
            _flushTimer = new Timer(FlushDueBatches, null, MaxBatchWaitMs, MaxBatchWaitMs);

            _logger.LogInformation("GenericSqlBatcher initialized with {BatchSize} max batch size", MaxBatchSize);
        }

        /// <summary>
        /// Queue a SQL operation for batch execution
        /// </summary>
        /// <param name="operationKey">Unique key to identify similar operations (e.g., "INSERT_Synonyms")</param>
        /// <param name="sql">SQL command text (can have parameters)</param>
        /// <param name="parameters">Anonymous object or Dictionary with parameters</param>
        /// <param name="commandType">Command type (default Text)</param>
        /// <param name="timeoutSeconds">Command timeout in seconds</param>
        public async Task QueueOperationAsync(
            string operationKey,
            string sql,
            object parameters,
            CommandType commandType = CommandType.Text,
            int timeoutSeconds = 30)
        {
            Interlocked.Increment(ref _totalOperationsQueued);
            _operationCounts.AddOrUpdate(operationKey, 1, (key, count) => count + 1);

            _logger.LogInformation(
                "Operation queued | Key={Key} | TotalQueued={Total} | KeyCount={KeyCount}",
                operationKey, _totalOperationsQueued, _operationCounts[operationKey]);

            var batch = _operations.GetOrAdd(operationKey,
                key => new BatchedOperation(key, sql, commandType, timeoutSeconds, _logger));

            batch.AddParameters(parameters);

            if (batch.Count >= MaxBatchSize)
            {
                _logger.LogInformation(
                    "Batch full | Key={Key} | Size={Size}/{MaxSize} | Triggering flush",
                    operationKey, batch.Count, MaxBatchSize);
                await FlushBatchAsync(operationKey);
            }
        }

        /// <summary>
        /// Execute an operation immediately (bypass batching)
        /// </summary>
        public async Task<int> ExecuteImmediateAsync(
            string sql,
            object parameters,
            CommandType commandType = CommandType.Text,
            int timeoutSeconds = 30,
            CancellationToken ct = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GenericSqlBatcher));

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
        /// Force flush all pending operations
        /// </summary>
        public async Task FlushAllAsync(CancellationToken ct = default)
        {
            if (_disposed) return;

            var operationKeys = _operations.Keys.ToList();
            _logger.LogDebug("Flushing all {Count} pending batches", operationKeys.Count);

            foreach (var key in operationKeys)
            {
                await FlushBatchAsync(key, ct);
            }
        }

        /// <summary>
        /// Flush a specific batch
        /// </summary>
        private async Task FlushBatchAsync(string operationKey, CancellationToken ct = default)
        {
            if (!_operations.TryRemove(operationKey, out var batch))
                return;

            if (batch.Count == 0)
                return;

            await ExecuteBatchAsync(batch, ct);
        }

        /// <summary>
        /// Execute a batched operation
        /// </summary>
        private async Task ExecuteBatchAsync(BatchedOperation batch, CancellationToken ct)
        {
            Interlocked.Increment(ref _totalBatchesExecuted);

            _logger.LogInformation(
                "EXECUTING BATCH | Key={Key} | Operations={Count} | TotalBatches={TotalBatches} | TotalQueued={TotalQueued}",
                batch.OperationKey, batch.Count, _totalBatchesExecuted, _totalOperationsQueued);

            var retryCount = 0;

            while (retryCount <= MaxRetries)
            {
                try
                {
                    await using var connection = new SqlConnection(_connectionString);
                    await connection.OpenAsync(ct);

                    // Generate batch SQL
                    var batchSql = batch.GenerateBatchSql();
                    var batchParameters = batch.GetBatchParameters();

                    _logger.LogDebug(
                        "Executing batch {OperationKey} with {Count} operations",
                        batch.OperationKey, batch.Count);

                    var result = await connection.ExecuteAsync(
                        new CommandDefinition(
                            batchSql,
                            batchParameters,
                            commandTimeout: batch.TimeoutSeconds,
                            commandType: batch.CommandType,
                            cancellationToken: ct));

                    _logger.LogInformation(
                        "Batch {OperationKey} executed successfully | Operations={Count} | RowsAffected={Rows}",
                        batch.OperationKey, batch.Count, result);

                    return;
                }
                catch (SqlException sqlEx) when (sqlEx.Number == 1205) // Deadlock
                {
                    retryCount++;
                    if (retryCount <= MaxRetries)
                    {
                        _logger.LogWarning(
                            sqlEx, "Deadlock detected on batch {OperationKey}, retry {Retry}/{MaxRetries}",
                            batch.OperationKey, retryCount, MaxRetries);
                        await Task.Delay(100 * retryCount, ct); // Exponential backoff
                    }
                    else
                    {
                        _logger.LogError(
                            sqlEx, "Batch {OperationKey} failed after {MaxRetries} retries",
                            batch.OperationKey, MaxRetries);
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex, "Batch {OperationKey} failed with {Count} operations",
                        batch.OperationKey, batch.Count);

                    // Attempt to save failed batch for recovery
                    await SaveFailedBatchForRecovery(batch, ex);
                    throw;
                }
            }
        }

        /// <summary>
        /// Timer callback for flushing due batches
        /// </summary>
        private async void FlushDueBatches(object? state)
        {
            if (_disposed) return;

            try
            {
                var now = DateTime.UtcNow;
                var dueBatches = _operations
                    .Where(kvp => kvp.Value.CreatedAt.AddMilliseconds(MaxBatchWaitMs) <= now)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in dueBatches)
                {
                    await FlushBatchAsync(key);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in batch flush timer");
            }
        }

        /// <summary>
        /// Save failed batch for manual recovery
        /// </summary>
        private async Task SaveFailedBatchForRecovery(BatchedOperation batch, Exception ex)
        {
            try
            {
                var recoveryTableSql = @"
                    IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'BatchRecovery')
                    CREATE TABLE dbo.BatchRecovery (
                        RecoveryId UNIQUEIDENTIFIER DEFAULT NEWID() PRIMARY KEY,
                        OperationKey NVARCHAR(200) NOT NULL,
                        BatchSql NVARCHAR(MAX) NOT NULL,
                        ParametersJson NVARCHAR(MAX) NULL,
                        ErrorMessage NVARCHAR(MAX) NULL,
                        OperationCount INT NOT NULL,
                        CreatedUtc DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
                    )";

                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                await connection.ExecuteAsync(recoveryTableSql);

                var insertSql = @"
                    INSERT INTO dbo.BatchRecovery
                    (OperationKey, BatchSql, ParametersJson, ErrorMessage, OperationCount)
                    VALUES (@OperationKey, @BatchSql, @ParametersJson, @ErrorMessage, @OperationCount)";

                await connection.ExecuteAsync(insertSql, new
                {
                    batch.OperationKey,
                    BatchSql = batch.SqlTemplate,
                    ParametersJson = JsonSerializer.Serialize(batch.GetAllParameters()),
                    ErrorMessage = ex.ToString(),
                    batch.Count
                });

                _logger.LogWarning(
                    "Saved failed batch {OperationKey} with {Count} operations to recovery table",
                    batch.OperationKey, batch.Count);
            }
            catch (Exception recoveryEx)
            {
                _logger.LogError(recoveryEx, "Failed to save batch for recovery");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                // Stop timer
                _flushTimer?.Dispose();

                // Flush remaining operations
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
        }

        /// <summary>
        /// Represents a batch of similar SQL operations
        /// </summary>
        private class BatchedOperation
        {
            public string OperationKey { get; }
            public string SqlTemplate { get; }
            public CommandType CommandType { get; }
            public int TimeoutSeconds { get; }
            public int Count => _parameterBatches.Count;
            public DateTime CreatedAt { get; }

            private readonly List<Dictionary<string, object>> _parameterBatches = new();
            private readonly ILogger _logger;

            public BatchedOperation(
                string operationKey,
                string sqlTemplate,
                CommandType commandType,
                int timeoutSeconds,
                ILogger logger)
            {
                OperationKey = operationKey;
                SqlTemplate = sqlTemplate;
                CommandType = commandType;
                TimeoutSeconds = timeoutSeconds;
                CreatedAt = DateTime.UtcNow;
                _logger = logger;
            }

            public void AddParameters(object parameters)
            {
                var paramDict = ConvertToDictionary(parameters);
                _parameterBatches.Add(paramDict);

                _logger.LogTrace(
                    "Added parameters to batch {OperationKey}, total: {Count}",
                    OperationKey, _parameterBatches.Count);
            }

            public string GenerateBatchSql()
            {
                if (_parameterBatches.Count == 0)
                    return string.Empty;

                var sb = new StringBuilder();

                for (int i = 0; i < _parameterBatches.Count; i++)
                {
                    if (i > 0) sb.AppendLine();

                    var parameterizedSql = ParameterizeSql(SqlTemplate, i);
                    sb.Append(parameterizedSql);

                    // Add semicolon between statements if needed
                    if (!SqlTemplate.TrimEnd().EndsWith(";"))
                        sb.Append(";");
                }

                return sb.ToString();
            }

            public object GetBatchParameters()
            {
                var parameters = new DynamicParameters();

                for (int batchIndex = 0; batchIndex < _parameterBatches.Count; batchIndex++)
                {
                    var paramDict = _parameterBatches[batchIndex];

                    foreach (var kvp in paramDict)
                    {
                        var paramName = $"p{batchIndex}_{kvp.Key}";
                        parameters.Add(paramName, kvp.Value);
                    }
                }

                return parameters;
            }

            public List<Dictionary<string, object>> GetAllParameters() =>
                new List<Dictionary<string, object>>(_parameterBatches);

            private string ParameterizeSql(string sql, int batchIndex)
            {
                var paramDict = _parameterBatches[batchIndex];
                var parameterizedSql = sql;

                foreach (var paramName in paramDict.Keys)
                {
                    var newParamName = $"@p{batchIndex}_{paramName}";
                    var oldParamName = $"@{paramName}";

                    parameterizedSql = parameterizedSql.Replace(oldParamName, newParamName);
                }

                return parameterizedSql;
            }

            private static Dictionary<string, object> ConvertToDictionary(object parameters)
            {
                if (parameters is Dictionary<string, object> dict)
                    return dict;

                if (parameters is IDictionary idict)
                {
                    var result = new Dictionary<string, object>();
                    foreach (System.Collections.DictionaryEntry entry in idict) // Fully qualified
                    {
                        result[entry.Key.ToString()!] = entry.Value!;
                    }
                    return result;
                }

                // Convert anonymous object to dictionary
                var propertyDict = new Dictionary<string, object>();
                var properties = parameters.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

                foreach (var prop in properties)
                {
                    propertyDict[prop.Name] = prop.GetValue(parameters)!;
                }

                return propertyDict;
            }
        }
    }
}