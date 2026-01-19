using DictionaryImporter.Sources.Common.Helper;
using DictionaryImporter.Sources.Common.Parsing;

namespace DictionaryImporter.Sources.EnglishChinese.Parsing
{
    public sealed class EnglishChineseDefinitionParser : ISourceDictionaryDefinitionParser
    {
        public string SourceCode => "ENG_CHN";

        public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Definition))
            {
                yield return SourceDataHelper.CreateFallbackParsedDefinition(entry);
                yield break;
            }

            var cleanedDefinition = TextProcessingHelper.CleanDefinition(
                entry.Definition,
                entry.Word,
                '⬄');

            if (!TextProcessingHelper.IsValidDefinition(cleanedDefinition))
            {
                yield return SourceDataHelper.CreateFallbackParsedDefinition(entry);
                yield break;
            }

            // ✅ ensure returned object has CrossReferences list initialized (never null)
            var parsed = TextProcessingHelper.CreateParsedDefinition(entry, cleanedDefinition);

            yield return parsed;
        }
    }
}