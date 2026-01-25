namespace DictionaryImporter.Gateway.Ai.Abstractions;

public sealed class AiExecutionOptions
{
    public int MaxTokens { get; init; } = 800;
    public double Temperature { get; init; } = 0.2;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    public AiExecutionMode Mode { get; init; } = AiExecutionMode.SingleBest;

    public int ParallelCalls { get; init; } = 3;

    public int BulkBatchSize { get; init; } = 20;
}