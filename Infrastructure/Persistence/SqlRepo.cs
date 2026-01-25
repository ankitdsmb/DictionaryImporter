using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public abstract class SqlRepo
    {
        protected readonly string ConnectionString;
        protected readonly ILogger Logger;

        protected SqlRepo(string connectionString, ILogger logger)
        {
            ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // NEW METHOD (added)
        protected async Task<T> WithConn<T>(
            Func<SqlConnection, Task<T>> work,
            CancellationToken ct,
            int timeoutSeconds = 60,
            bool swallowException = true,
            T fallback = default!)
        {
            try
            {
                await using var conn = new SqlConnection(ConnectionString);
                await conn.OpenAsync(ct);
                return await work(conn);
            }
            catch (OperationCanceledException)
            {
                return fallback;
            }
            catch (Exception ex)
            {
                if (!swallowException)
                    throw;

                Logger.LogError(ex, "{Repo} SQL operation failed.", GetType().Name);
                return fallback;
            }
        }

        // NEW METHOD (added)
        protected async Task WithConn(
            Func<SqlConnection, Task> work,
            CancellationToken ct,
            int timeoutSeconds = 60,
            bool swallowException = true)
        {
            await WithConn(async c =>
            {
                await work(c);
                return 0;
            }, ct, timeoutSeconds, swallowException, 0);
        }

        // NEW METHOD (added)
        protected async Task WithTx(
            Func<SqlConnection, SqlTransaction, Task> work,
            CancellationToken ct,
            IsolationLevel isolation = IsolationLevel.ReadCommitted,
            int timeoutSeconds = 60)
        {
            await WithConn(async conn =>
            {
                using var tx = conn.BeginTransaction(isolation);
                await work(conn, tx);
                tx.Commit();
            }, ct, timeoutSeconds, swallowException: true);
        }

        // NEW METHOD (added)
        protected static string Normalize(string? s, string fallback = "")
            => string.IsNullOrWhiteSpace(s) ? fallback : s.Trim();

        // NEW METHOD (added)
        protected static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        // NEW METHOD (added)
        protected static long Clamp(long v, long min, long max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        // NEW METHOD (added)
        protected static string Trunc(string? s, int max)
        {
            var t = (s ?? string.Empty).Trim();
            if (max <= 0) return t;
            return t.Length > max ? t.Substring(0, max) : t;
        }
    }
}
