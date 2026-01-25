namespace DictionaryImporter.Infrastructure.Qa;

/// <summary>
///     Single row of QA output, normalized across all QA SPs.
/// </summary>
public sealed class QaSummaryRow
{
    public string Phase { get; init; } = "";
    public string CheckName { get; init; } = "";
    public string Status { get; init; } = "";
    public string Details { get; init; } = "";
}