using System.Data;

namespace DictionaryImporter.Infrastructure.Qa;

public sealed class QaStoredProcedureCheck : IQaCheck
{
    private readonly string _connectionString;
    private readonly object? _parameters;
    private readonly string _procedureName;

    public QaStoredProcedureCheck(
        string name,
        string phase,
        string procedureName,
        string connectionString,
        object? parameters = null)
    {
        Name = name;
        Phase = phase;
        _procedureName = procedureName;
        _connectionString = connectionString;
        _parameters = parameters;
    }

    public string Name { get; }
    public string Phase { get; }

    public async Task<IReadOnlyList<QaSummaryRow>> ExecuteAsync(
        CancellationToken ct)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var rows =
            await conn.QueryAsync<dynamic>(
                _procedureName,
                _parameters,
                commandType: CommandType.StoredProcedure);

        var results = new List<QaSummaryRow>();

        foreach (var row in rows)
            results.Add(new QaSummaryRow
            {
                Phase = Phase,
                CheckName = Name,
                Status = row.OverallQaStatus,
                Details = row.LocaleCode != null
                    ? $"Locale={row.LocaleCode}"
                    : null
            });

        return results;
    }
}