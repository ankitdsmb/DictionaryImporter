using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Data;

namespace DictionaryImporter.Infrastructure.Persistence.Batched
{
    /// <summary>
    /// Extension methods for Dapper that automatically batch operations
    /// </summary>
    public static class BatchedDapperExtensions
    {
        private static GenericSqlBatcher? _batcher;
        private static readonly ConcurrentDictionary<string, bool> _batchableOperations = new();

        /// <summary>
        /// Initialize the batcher (should be called during application startup)
        /// </summary>
        public static void Initialize(GenericSqlBatcher batcher)
        {
            _batcher = batcher;
        }

        /// <summary>
        /// Execute a SQL command with automatic batching
        /// </summary>
        public static async Task<int> ExecuteBatchedAsync(
            this SqlConnection connection,
            string sql,
            object? param = null,
            IDbTransaction? transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null)
        {
            if (_batcher == null)
                throw new InvalidOperationException("BatchedDapperExtensions not initialized. Call Initialize() first.");

            // Generate operation key from SQL hash
            var operationKey = GenerateOperationKey(sql, commandType ?? CommandType.Text);

            // Queue for batching (non-transactional operations only)
            if (transaction == null && ShouldBatch(sql))
            {
                await _batcher.QueueOperationAsync(
                    operationKey,
                    sql,
                    param ?? new { },
                    commandType ?? CommandType.Text,
                    commandTimeout ?? 30);

                return 1; // Return optimistic result
            }

            // Execute immediately for transactional operations or non-batchable SQL
            return await connection.ExecuteAsync(sql, param, transaction, commandTimeout, commandType);
        }

        /// <summary>
        /// Execute multiple SQL commands with batching
        /// </summary>
        public static async Task<int> ExecuteBatchedAsync(
            this SqlConnection connection,
            string sql,
            IEnumerable<object> paramList,
            IDbTransaction? transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null)
        {
            if (_batcher == null)
                throw new InvalidOperationException("BatchedDapperExtensions not initialized. Call Initialize() first.");

            // Generate operation key from SQL hash
            var operationKey = GenerateOperationKey(sql, commandType ?? CommandType.Text);

            // Queue each parameter set for batching
            if (transaction == null && ShouldBatch(sql))
            {
                foreach (var param in paramList)
                {
                    await _batcher.QueueOperationAsync(
                        operationKey,
                        sql,
                        param,
                        commandType ?? CommandType.Text,
                        commandTimeout ?? 30);
                }

                return paramList.Count(); // Return optimistic result
            }

            // Execute immediately
            return await connection.ExecuteAsync(sql, paramList, transaction, commandTimeout, commandType);
        }

        /// <summary>
        /// Query with automatic batching (for SELECT operations)
        /// Note: SELECT operations are not batched, only executed
        /// </summary>
        public static async Task<IEnumerable<T>> QueryBatchedAsync<T>(
            this SqlConnection connection,
            string sql,
            object? param = null,
            IDbTransaction? transaction = null,
            int? commandTimeout = null,
            CommandType? commandType = null)
        {
            // Queries are not batched, execute immediately
            return await connection.QueryAsync<T>(
                sql,
                param,
                transaction,
                commandTimeout,
                commandType);
        }

        /// <summary>
        /// Flush all pending batched operations
        /// </summary>
        public static async Task FlushBatchesAsync(CancellationToken ct = default)
        {
            if (_batcher == null)
                return;

            await _batcher.FlushAllAsync(ct);
        }

        /// <summary>
        /// Check if SQL should be batched
        /// </summary>
        private static bool ShouldBatch(string sql)
        {
            // Cache the result for performance
            return _batchableOperations.GetOrAdd(sql, ShouldBatchInternal);
        }

        private static bool ShouldBatchInternal(string sql)
        {
            var normalizedSql = sql.Trim().ToUpperInvariant();

            // Batch INSERT, UPDATE, DELETE operations
            return normalizedSql.StartsWith("INSERT") ||
                   normalizedSql.StartsWith("UPDATE") ||
                   normalizedSql.StartsWith("DELETE") ||
                   normalizedSql.StartsWith("MERGE");
        }

        /// <summary>
        /// Generate a unique operation key from SQL
        /// </summary>
        private static string GenerateOperationKey(string sql, CommandType commandType)
        {
            var sqlHash = Math.Abs(sql.GetHashCode());
            return $"{commandType}_{sqlHash}";
        }
    }
}