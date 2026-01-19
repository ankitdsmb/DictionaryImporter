using DictionaryImporter.Sources.Common.Helper;

namespace DictionaryImporter.Sources
{
    /// <summary>
    /// Base class for dictionary definition parsers with common functionality.
    /// </summary>
    public abstract class BaseDefinitionParser : IDictionaryDefinitionParser
    {
        /// <summary>
        /// Parses a dictionary entry into one or more parsed definitions.
        /// </summary>
        public abstract IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry);

        /// <summary>
        /// Creates a fallback parsed definition when parsing fails.
        /// </summary>
        protected virtual ParsedDefinition CreateFallbackDefinition(DictionaryEntry entry)
        {
            return SourceDataHelper.CreateFallbackParsedDefinition(entry);
        }

        /// <summary>
        /// Cleans and processes a definition string.
        /// </summary>
        protected virtual string CleanDefinition(string definition, DictionaryEntry entry)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return definition ?? string.Empty;

            var cleaned = definition;

            // Remove common markers
            cleaned = TextExtractionHelper.RemoveIpaMarkers(cleaned);
            cleaned = TextExtractionHelper.RemoveSyllableMarkers(cleaned);
            cleaned = TextExtractionHelper.RemovePosMarkers(cleaned);

            // Remove headword if present
            if (!string.IsNullOrWhiteSpace(entry.Word))
            {
                cleaned = TextExtractionHelper.RemoveHeadwordFromDefinition(cleaned, entry.Word);
            }

            // Remove specific separators
            cleaned = TextExtractionHelper.RemoveSeparators(cleaned, '⬄');

            // Normalize whitespace
            cleaned = TextExtractionHelper.NormalizeWhitespace(cleaned);

            return cleaned;
        }

        /// <summary>
        /// Validates if a cleaned definition is usable.
        /// </summary>
        protected virtual bool IsValidDefinition(string definition)
        {
            if (string.IsNullOrWhiteSpace(definition))
                return false;

            if (TextExtractionHelper.IsPosOnly(definition))
                return false;

            return true;
        }

        /// <summary>
        /// Creates a parsed definition with the cleaned text.
        /// </summary>
        protected virtual ParsedDefinition CreateParsedDefinition(
            DictionaryEntry entry,
            string cleanedDefinition)
        {
            return new ParsedDefinition
            {
                Definition = cleanedDefinition,
                RawFragment = entry.Definition,
                SenseNumber = entry.SenseNumber,
                Domain = null,
                UsageLabel = null,
                CrossReferences = new List<CrossReference>(),
                Synonyms = null,
                Alias = null
            };
        }
    }
}