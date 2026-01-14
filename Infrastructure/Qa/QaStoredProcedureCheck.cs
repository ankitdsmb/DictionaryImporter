using System.Data;

namespace DictionaryImporter.Infrastructure.Qa;

public sealed class QaStoredProcedureCheck(
    string name,
    string phase,
    string procedureName,
    string connectionString,
    object? parameters = null)
    : IQaCheck
{
    public string Name { get; } = name;
    public string Phase { get; } = phase;

    public async Task<IReadOnlyList<QaSummaryRow>> ExecuteAsync(
        CancellationToken ct)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);

        var rows =
            await conn.QueryAsync<dynamic>(
                procedureName,
                parameters,
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