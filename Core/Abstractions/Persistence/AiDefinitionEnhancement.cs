namespace DictionaryImporter.Core.Persistence
{
    public sealed class AiDefinitionEnhancement
    {
        public long ParsedDefinitionId { get; set; }
        public string OriginalDefinition { get; set; } = string.Empty;
        public string AiEnhancedDefinition { get; set; } = string.Empty;
        public string AiNotesJson { get; set; } = "{}";
        public string Provider { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
    }
}