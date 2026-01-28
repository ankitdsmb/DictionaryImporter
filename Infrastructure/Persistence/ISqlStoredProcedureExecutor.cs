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