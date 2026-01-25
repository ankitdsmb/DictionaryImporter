using System;
using System.Collections.Generic;
using System.Data;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;

namespace DictionaryImporter.Infrastructure.Persistence;

public interface ISqlStoredProcedureExecutor
{
    Task<int> ExecuteAsync(
        string spName,
        object? param,
        CancellationToken ct,
        IDbTransaction? tx = null,
        int? timeoutSeconds = null);

    Task<T?> QuerySingleOrDefaultAsync<T>(
        string spName,
        object? param,
        CancellationToken ct,
        IDbTransaction? tx = null,
        int? timeoutSeconds = null);

    Task<T> ExecuteScalarAsync<T>(
        string spName,
        object? param,
        CancellationToken ct,
        IDbTransaction? tx = null,
        int? timeoutSeconds = null);

    Task<IReadOnlyList<T>> QueryAsync<T>(
        string spName,
        object? param,
        CancellationToken ct,
        IDbTransaction? tx = null,
        int? timeoutSeconds = null);

    Task WithConnectionAsync(Func<SqlConnection, CancellationToken, Task> action, CancellationToken ct);
}

public sealed class SqlStoredProcedureExecutor : ISqlStoredProcedureExecutor
{
    private readonly string _connectionString;

    public SqlStoredProcedureExecutor(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<int> ExecuteAsync(
        string spName,
        object? param,
        CancellationToken ct,
        IDbTransaction? tx = null,
        int? timeoutSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(spName))
            return 0;

        var conn = tx?.Connection ?? await OpenConnectionAsync(ct);

        try
        {
            var dp = ToDynamicParameters(param);

            return await conn.ExecuteAsync(new CommandDefinition(
                spName,
                dp,
                transaction: tx,
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct,
                commandTimeout: timeoutSeconds));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return 0;
        }
        finally
        {
            if (tx is null)
                await SafeDisposeAsync(conn);
        }
    }

    public async Task<T?> QuerySingleOrDefaultAsync<T>(
        string spName,
        object? param,
        CancellationToken ct,
        IDbTransaction? tx = null,
        int? timeoutSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(spName))
            return default;

        var conn = tx?.Connection ?? await OpenConnectionAsync(ct);

        try
        {
            var dp = ToDynamicParameters(param);

            return await conn.QuerySingleOrDefaultAsync<T>(new CommandDefinition(
                spName,
                dp,
                transaction: tx,
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct,
                commandTimeout: timeoutSeconds));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return default;
        }
        finally
        {
            if (tx is null)
                await SafeDisposeAsync(conn);
        }
    }

    public async Task<T> ExecuteScalarAsync<T>(
        string spName,
        object? param,
        CancellationToken ct,
        IDbTransaction? tx = null,
        int? timeoutSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(spName))
            return default!;

        var conn = tx?.Connection ?? await OpenConnectionAsync(ct);

        try
        {
            var dp = ToDynamicParameters(param);

            return await conn.ExecuteScalarAsync<T>(new CommandDefinition(
                spName,
                dp,
                transaction: tx,
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct,
                commandTimeout: timeoutSeconds));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return default!;
        }
        finally
        {
            if (tx is null)
                await SafeDisposeAsync(conn);
        }
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        string spName,
        object? param,
        CancellationToken ct,
        IDbTransaction? tx = null,
        int? timeoutSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(spName))
            return Array.Empty<T>();

        var conn = tx?.Connection ?? await OpenConnectionAsync(ct);

        try
        {
            var dp = ToDynamicParameters(param);

            var rows = await conn.QueryAsync<T>(new CommandDefinition(
                spName,
                dp,
                transaction: tx,
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct,
                commandTimeout: timeoutSeconds));

            return rows.AsList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Array.Empty<T>();
        }
        finally
        {
            if (tx is null)
                await SafeDisposeAsync(conn);
        }
    }

    public async Task WithConnectionAsync(Func<SqlConnection, CancellationToken, Task> action, CancellationToken ct)
    {
        if (action is null)
            return;

        await using var conn = await OpenConnectionAsync(ct);
        await action(conn, ct);
    }

    private async Task<SqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    private static async Task SafeDisposeAsync(IDbConnection? conn)
    {
        if (conn is IAsyncDisposable ad)
        {
            await ad.DisposeAsync();
            return;
        }

        conn?.Dispose();
    }

    // NEW METHOD (added)
    private static DynamicParameters? ToDynamicParameters(object? param)
    {
        if (param is null)
            return null;

        if (param is DynamicParameters dpAlready)
            return dpAlready;

        var dp = new DynamicParameters();

        if (param is IEnumerable<KeyValuePair<string, object?>> kvps)
        {
            foreach (var kv in kvps)
            {
                AddDynamic(dp, kv.Key, kv.Value);
            }

            return dp;
        }

        var t = param.GetType();
        var props = t.GetProperties(BindingFlags.Instance | BindingFlags.Public);

        foreach (var p in props)
        {
            if (!p.CanRead)
                continue;

            var name = p.Name;
            object? value;

            try
            {
                value = p.GetValue(param);
            }
            catch
            {
                continue;
            }

            AddDynamic(dp, name, value);
        }

        return dp;
    }

    // NEW METHOD (added)
    private static void AddDynamic(DynamicParameters dp, string name, object? value)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        // IMPORTANT: TVPs (dt.AsTableValuedParameter(...)) implement ICustomQueryParameter.
        // They MUST be added into DynamicParameters to be handled correctly by Dapper.
        if (value is SqlMapper.ICustomQueryParameter custom)
        {
            dp.Add(name, custom);
            return;
        }

        dp.Add(name, value);
    }
}