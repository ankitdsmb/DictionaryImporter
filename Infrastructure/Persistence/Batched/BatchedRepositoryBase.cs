using Microsoft.Extensions.Logging;
using System.Data;
using System.Reflection;

namespace DictionaryImporter.Infrastructure.Persistence.Batched
{
    /// <summary>
    /// Base class for repositories that automatically batch SQL operations
    /// </summary>
    public abstract class BatchedRepositoryBase : IDisposable
    {
        protected readonly string ConnectionString;
        protected readonly ILogger Logger;
        protected readonly GenericSqlBatcher Batcher;
        private bool _disposed;

        protected BatchedRepositoryBase(
            string connectionString,
            ILogger logger,
            GenericSqlBatcher batcher)
        {
            ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Batcher = batcher ?? throw new ArgumentNullException(nameof(batcher));
        }

        /// <summary>
        /// Execute SQL with automatic batching
        /// </summary>
        protected async Task<int> ExecuteBatchedAsync(
            string sql,
            object parameters,
            CommandType commandType = CommandType.Text,
            int timeoutSeconds = 30,
            CancellationToken ct = default)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);

            // Generate operation key from caller info
            var operationKey = GetCallerOperationKey(sql);

            await Batcher.QueueOperationAsync(
                operationKey,
                sql,
                parameters,
                commandType,
                timeoutSeconds);

            return 1; // Optimistic result
        }

        /// <summary>
        /// Execute SQL immediately (bypass batching)
        /// </summary>
        protected async Task<int> ExecuteImmediateAsync(
            string sql,
            object parameters,
            CommandType commandType = CommandType.Text,
            int timeoutSeconds = 30,
            CancellationToken ct = default)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);

            return await Batcher.ExecuteImmediateAsync(sql, parameters, commandType, timeoutSeconds, ct);
        }

        /// <summary>
        /// Query data (not batched)
        /// </summary>
        protected async Task<IEnumerable<T>> QueryAsync<T>(
            string sql,
            object? parameters = null,
            CommandType commandType = CommandType.Text,
            int timeoutSeconds = 30,
            CancellationToken ct = default)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);

            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(ct);

            return await connection.QueryAsync<T>(
                new CommandDefinition(
                    sql,
                    parameters,
                    commandTimeout: timeoutSeconds,
                    commandType: commandType,
                    cancellationToken: ct));
        }

        /// <summary>
        /// Query single record
        /// </summary>
        protected async Task<T?> QuerySingleAsync<T>(
            string sql,
            object? parameters = null,
            CommandType commandType = CommandType.Text,
            int timeoutSeconds = 30,
            CancellationToken ct = default)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);

            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(ct);

            return await connection.QueryFirstOrDefaultAsync<T>(
                new CommandDefinition(
                    sql,
                    parameters,
                    commandTimeout: timeoutSeconds,
                    commandType: commandType,
                    cancellationToken: ct));
        }

        /// <summary>
        /// Flush all pending batched operations
        /// </summary>
        public async Task FlushBatchesAsync(CancellationToken ct = default)
        {
            if (_disposed) return;

            await Batcher.FlushAllAsync(ct);
        }

        /// <summary>
        /// Get operation key from caller method info
        /// </summary>
        private string GetCallerOperationKey(string sql)
        {
            var stackTrace = new StackTrace();
            var frame = stackTrace.GetFrame(2); // Skip current and parent method

            if (frame != null)
            {
                var method = frame.GetMethod();
                var className = method?.DeclaringType?.Name ?? "Unknown";
                var methodName = method?.Name ?? "Unknown";

                // Create hash from SQL and method info
                var key = $"{className}_{methodName}_{Math.Abs(sql.GetHashCode())}";
                return key;
            }

            return $"Unknown_{Math.Abs(sql.GetHashCode())}";
        }

        /// <summary>
        /// Dispose pattern
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Flush any remaining batches
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        FlushBatchesAsync(cts.Token).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Error during repository disposal");
                    }
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}