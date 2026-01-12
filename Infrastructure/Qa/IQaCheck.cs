namespace DictionaryImporter.Infrastructure.Qa
{
    public interface IQaCheck
    {
        string Name { get; }
        string Phase { get; }

        Task<IReadOnlyList<QaSummaryRow>> ExecuteAsync(
            CancellationToken ct);
    }
}
