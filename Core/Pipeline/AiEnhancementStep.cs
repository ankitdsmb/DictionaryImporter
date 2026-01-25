using DictionaryImporter.Core.Abstractions.Persistence;
using DictionaryImporter.Gateway.Ai.Abstractions;

namespace DictionaryImporter.Core.Pipeline;

public sealed class AiEnhancementStep(
    IAiGateway aiGateway,
    IAiAnnotationRepository repo,
    IConfiguration configuration,
    ILogger<AiEnhancementStep> logger)
{
    private const string Root = "AiGateway";

    private readonly bool _enabled =
        configuration.GetValue<bool>($"{Root}:EnablePipelineSteps:AiEnhancement", false);

    private readonly int _take =
        configuration.GetValue<int>($"{Root}:AiEnhancement:Take", 50);

    private readonly string _provider =
        configuration.GetValue<string>($"{Root}:AiEnhancement:Provider", "Ollama");

    private readonly string _model =
        configuration.GetValue<string>($"{Root}:AiEnhancement:Model", "llama3.1");

    private readonly int _maxTokens =
        configuration.GetValue<int>($"{Root}:AiEnhancement:MaxTokens", 250);

    private readonly double _temperature =
        configuration.GetValue<double>($"{Root}:AiEnhancement:Temperature", 0.2);

    private readonly int _parallelCalls =
        configuration.GetValue<int>($"{Root}:AiEnhancement:ParallelCalls", 1);

    private readonly int _bulkBatchSize =
        configuration.GetValue<int>($"{Root}:AiEnhancement:BulkBatchSize", 1);

    private readonly AiExecutionMode _mode =
        Enum.TryParse(configuration.GetValue<string>($"{Root}:AiEnhancement:Mode", "SingleBest"),
            ignoreCase: true,
            out AiExecutionMode parsedMode)
            ? parsedMode
            : AiExecutionMode.SingleBest;

    public async Task ExecuteAsync(string sourceCode, CancellationToken ct)
    {
        if (!_enabled)
        {
            logger.LogInformation("AI enhancement disabled. Source={Source}", sourceCode);
            return;
        }

        var candidates = await repo.GetDefinitionCandidatesAsync(sourceCode, _take, ct);

        if (candidates.Count == 0)
        {
            logger.LogInformation("No AI enhancement candidates found. Source={Source}", sourceCode);
            return;
        }

        logger.LogInformation(
            "AI enhancement started. Source={Source}, Count={Count}",
            sourceCode, candidates.Count);

        var requests = new List<AiGatewayRequest>(candidates.Count);

        foreach (var c in candidates)
        {
            var original = c.DefinitionText?.Trim();
            if (string.IsNullOrWhiteSpace(original))
                continue;

            requests.Add(new AiGatewayRequest
            {
                Task = AiTaskType.RewriteDefinition,
                Capability = AiCapability.Text,

                ProviderName = _provider,
                Model = _model,

                SourceCode = sourceCode,
                Language = "en",
                CorrelationId = $"def:{c.ParsedDefinitionId}",

                SystemPrompt =
                    "You are an expert dictionary editor. Improve grammar and clarity without changing meaning.",

                Prompt = BuildRewritePrompt(original),

                Options = new AiExecutionOptions
                {
                    Mode = _mode,
                    ParallelCalls = _parallelCalls,
                    MaxTokens = _maxTokens,
                    Temperature = _temperature,
                    BulkBatchSize = _bulkBatchSize
                }
            });
        }

        if (requests.Count == 0)
        {
            logger.LogInformation("No valid AI requests created. Source={Source}", sourceCode);
            return;
        }

        var responses = await aiGateway.ExecuteBulkAsync(requests, ct);

        var enhancements = new List<AiDefinitionEnhancement>();

        for (var i = 0; i < responses.Count; i++)
        {
            var req = requests[i];
            var res = responses[i];

            var parsedDefinitionId = ExtractParsedDefinitionId(req.CorrelationId);

            if (!res.IsSuccess)
            {
                logger.LogWarning(
                    "AI enhancement failed. Source={Source}, ParsedDefinitionId={Id}, Error={Error}",
                    sourceCode, parsedDefinitionId, res.Error);

                continue;
            }

            var improved = (res.OutputText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(improved))
                continue;

            var original =
                candidates.FirstOrDefault(x => x.ParsedDefinitionId == parsedDefinitionId)?.DefinitionText ?? "";

            enhancements.Add(new AiDefinitionEnhancement
            {
                ParsedDefinitionId = parsedDefinitionId,
                OriginalDefinition = original,
                AiEnhancedDefinition = improved,

                // optional: store provider debug info here later
                AiNotesJson = "{}",

                Provider = res.FinalProvider ?? "unknown",
                Model = res.FinalModel ?? "unknown"
            });
        }

        if (enhancements.Count == 0)
        {
            logger.LogInformation("No AI enhancements to save. Source={Source}", sourceCode);
            return;
        }

        await repo.SaveAiEnhancementsAsync(sourceCode, enhancements, ct);

        logger.LogInformation(
            "AI enhancement finished. Source={Source}, Saved={SavedCount}",
            sourceCode, enhancements.Count);
    }

    private static string BuildRewritePrompt(string definition)
    {
        return
            $"""
             Fix grammar and rewrite this dictionary definition into clean natural English.

             Rules:
             1) Do not change meaning.
             2) Keep it short and dictionary-style.
             3) Remove broken punctuation, strange symbols, and duplicated spaces.
             4) Return ONLY the improved definition text (no explanation).

             Definition:
             {definition}
             """;
    }

    private static long ExtractParsedDefinitionId(string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
            return 0;

        var parts = correlationId.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return 0;

        return long.TryParse(parts[1], out var id) ? id : 0;
    }
}