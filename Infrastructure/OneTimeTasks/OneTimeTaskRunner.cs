namespace DictionaryImporter.Infrastructure.OneTimeTasks
{
    /// <summary>
    ///     Executes selected one-time database tasks explicitly.
    ///     Never runs automatically.
    /// </summary>
    public sealed class OneTimeTaskRunner(
        IEnumerable<IOneTimeDatabaseTask> tasks,
        ILogger<OneTimeTaskRunner> logger)
    {
        public async Task RunAsync(
            IEnumerable<string> taskNames,
            CancellationToken ct)
        {
            var selected =
                tasks.Where(t => taskNames.Contains(t.Name));

            foreach (var task in selected)
            {
                ct.ThrowIfCancellationRequested();

                logger.LogInformation(
                    "One-time task started | Task={Task}",
                    task.Name);

                await task.ExecuteAsync(ct);

                logger.LogInformation(
                    "One-time task completed | Task={Task}",
                    task.Name);
            }
        }
    }
}