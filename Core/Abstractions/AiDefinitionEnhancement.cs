namespace DictionaryImporter.Core.Abstractions
{
    public sealed class AiDefinitionEnhancement
    {
        public long ParsedDefinitionId { get; set; }

        public string OriginalDefinition { get; set; } = string.Empty;
        public string AiEnhancedDefinition { get; set; } = string.Empty;

        public string AiNotesJson { get; set; } = "{}";

        public string Provider { get; set; } = "RuleBased";
        public string Model { get; set; } = "DictionaryRewriteV1";
    }
}