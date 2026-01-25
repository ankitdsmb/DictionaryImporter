namespace DictionaryImporter.Core.Abstractions
{
    public sealed class AiDefinitionCandidate
    {
        public long ParsedDefinitionId { get; init; }

        public string DefinitionText { get; init; } = string.Empty;

        // REQUIRED by RuleBasedDefinitionEnhancementStep
        public string MeaningTitle { get; init; } = string.Empty;

        // kept for compatibility (some pipelines pass empty string)
        public string ExampleText { get; init; } = string.Empty;
    }
}