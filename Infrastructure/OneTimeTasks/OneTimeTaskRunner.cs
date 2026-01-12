namespace DictionaryImporter.Infrastructure.OneTimeTasks;

/// <summary>
///     Executes selected one-time database tasks explicitly.
///     Never runs automatically.
/// </summary>
public sealed class OneTimeTaskRunner
{
    private readonly ILogger<OneTimeTaskRunner> _logger;
    private readonly IEnumerable<IOneTimeDatabaseTask> _tasks;

    public OneTimeTaskRunner(
        IEnumerable<IOneTimeDatabaseTask> tasks,
        ILogger<OneTimeTaskRunner> logger)
    {
        _tasks = tasks;
        _logger = logger;
    }

    public async Task RunAsync(
        IEnumerable<string> taskNames,
        CancellationToken ct)
    {
        var selected =
            _tasks.Where(t => taskNames.Contains(t.Name));

        foreach (var task in selected)
        {
            ct.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "One-time task started | Task={Task}",
                task.Name);

            await task.ExecuteAsync(ct);

            _logger.LogInformation(
                "One-time task completed | Task={Task}",
                task.Name);
        }
    }
}