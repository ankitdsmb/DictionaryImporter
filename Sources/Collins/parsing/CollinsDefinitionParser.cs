using System;
using System.Collections.Generic;
using System.Linq;
using DictionaryImporter.Sources.Common.Helper;
using DictionaryImporter.Sources.Common.Parsing;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Sources.Collins.Parsing
{
    public sealed class CollinsDefinitionParser : ISourceDictionaryDefinitionParser
    {
        private readonly ILogger<CollinsDefinitionParser> _logger;

        public CollinsDefinitionParser(ILogger<CollinsDefinitionParser> logger = null)
        {
            _logger = logger;
        }

        public string SourceCode => "ENG_COLLINS";

        public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Definition))
            {
                yield return CreateFallbackParsedDefinition(entry);
                yield break;
            }

            var results = new List<ParsedDefinition>();

            try
            {
                var definition = entry.Definition;
                var parsedData = CollinsParsingHelper.ParseCollinsEntry(definition);

                // Build full definition with metadata
                var fullDefinition = BuildFullDefinition(parsedData);

                // Extract synonyms
                var synonyms = CollinsParsingHelper.ExtractSynonyms(definition, parsedData.Examples);

                results.Add(new ParsedDefinition
                {
                    MeaningTitle = entry.Word ?? "unnamed sense",
                    Definition = fullDefinition,
                    RawFragment = entry.Definition,
                    SenseNumber = parsedData.SenseNumber,
                    Domain = parsedData.PrimaryDomain,
                    UsageLabel = BuildUsageLabel(parsedData),
                    CrossReferences = parsedData.CrossReferences.ToList(),
                    Synonyms = synonyms.Count > 0 ? synonyms : null,
                    Alias = parsedData.PhrasalVerbInfo.IsPhrasalVerb
                           ? $"{parsedData.PhrasalVerbInfo.Verb} {parsedData.PhrasalVerbInfo.Particle}"
                           : null,
                    Examples = parsedData.Examples.ToList()
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to parse Collins entry: {Word}", entry.Word);
                results.Clear();
                results.Add(CreateFallbackParsedDefinition(entry));
            }

            foreach (var item in results)
                yield return item;
        }

        private string BuildFullDefinition(CollinsParsedData data)
        {
            var parts = new List<string>();

            // Add main definition
            if (!string.IsNullOrWhiteSpace(data.CleanDefinition))
                parts.Add(data.CleanDefinition);
            else if (!string.IsNullOrWhiteSpace(data.MainDefinition))
                parts.Add(data.MainDefinition);

            // Add POS if available
            if (!string.IsNullOrWhiteSpace(data.PartOfSpeech) && data.PartOfSpeech != "unk")
                parts.Insert(0, $"【POS】{data.PartOfSpeech}");

            // Add domain labels
            if (data.DomainLabels.Count > 0)
                parts.Add($"【Domains】{string.Join(", ", data.DomainLabels)}");

            // Add usage patterns
            if (data.UsagePatterns.Count > 0)
                parts.Add($"【Patterns】{string.Join("; ", data.UsagePatterns)}");

            // Add phrasal verb info
            if (data.PhrasalVerbInfo.IsPhrasalVerb)
            {
                parts.Add($"【PhrasalVerb】{data.PhrasalVerbInfo.Verb} {data.PhrasalVerbInfo.Particle}");
                if (data.PhrasalVerbInfo.Patterns.Count > 0)
                    parts.Add($"【PhrasalPatterns】{string.Join("; ", data.PhrasalVerbInfo.Patterns)}");
            }

            return string.Join("\n", parts).Trim();
        }

        private string BuildUsageLabel(CollinsParsedData data)
        {
            var labels = new List<string>();

            if (!string.IsNullOrWhiteSpace(data.PartOfSpeech) && data.PartOfSpeech != "unk")
                labels.Add(data.PartOfSpeech);

            if (data.DomainLabels.Count > 0)
                labels.AddRange(data.DomainLabels.Take(2));

            return labels.Count > 0 ? string.Join(", ", labels) : null;
        }

        private ParsedDefinition CreateFallbackParsedDefinition(DictionaryEntry entry)
        {
            return new ParsedDefinition
            {
                MeaningTitle = entry.Word ?? "unnamed sense",
                Definition = entry.Definition ?? string.Empty,
                RawFragment = entry.Definition ?? string.Empty,
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




//// File: Sources/Collins/Parsing/CollinsDefinitionParser.cs
//using DictionaryImporter.Domain.Models;
//using DictionaryImporter.Sources.Common.Helper;
//using DictionaryImporter.Sources.Common.Parsing;
//using System.Collections.Generic;

//namespace DictionaryImporter.Sources.Collins.Parsing
//{
//    public sealed class CollinsDefinitionParser : ISourceDictionaryDefinitionParser
//    {
//        public string SourceCode => "ENG_COLLINS";

//        public IEnumerable<ParsedDefinition> Parse(DictionaryEntry entry)
//        {
//            // ✅ never return empty
//            if (string.IsNullOrWhiteSpace(entry.Definition))
//            {
//                yield return SourceDataHelper.CreateFallbackParsedDefinition(entry);
//                yield break;
//            }

//            var definition = entry.Definition;
//            var mainDefinition = CollinsSourceDataHelper.ExtractMainDefinition(definition);
//            var examples = CollinsSourceDataHelper.ExtractExamples(definition);
//            var domain = CollinsSourceDataHelper.ExtractDomain(definition);
//            var grammar = CollinsSourceDataHelper.ExtractGrammar(definition);
//            var crossRefs = CollinsSourceDataHelper.ExtractCrossReferences(definition);

//            var parsedDefinition = new ParsedDefinition
//            {
//                MeaningTitle = entry.Word ?? "unnamed sense",
//                Definition = mainDefinition,
//                RawFragment = entry.Definition,
//                SenseNumber = entry.SenseNumber,
//                Domain = domain,
//                UsageLabel = grammar,
//                CrossReferences = crossRefs,
//                Synonyms = CollinsSourceDataHelper.ExtractSynonymsFromExamples(examples),
//                Alias = null
//            };

//            // ✅ attach examples
//            parsedDefinition.Examples = examples;

//            yield return parsedDefinition;
//        }
//    }
//}