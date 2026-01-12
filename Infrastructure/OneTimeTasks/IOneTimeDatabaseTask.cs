namespace DictionaryImporter.Infrastructure.OneTimeTasks;

/// <summary>
///     Represents a controlled, explicitly executed
///     one-time database operation (migration / repair).
/// </summary>
public interface IOneTimeDatabaseTask
{
    string Name { get; }

    Task ExecuteAsync(CancellationToken ct);
}