// Update the existing DictionaryParsedDefinitionProcessor class
using Dapper;
using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Core.Parsing;
using DictionaryImporter.Core.Persistence;
using DictionaryImporter.Domain.Models;
using DictionaryImporter.Infrastructure.Persistence;
using DictionaryImporter.Sources.Collins.Parsing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DictionaryImporter.Infrastructure.Parsing
{
    public sealed class DictionaryParsedDefinitionProcessor
    {
        private readonly string _connectionString;
        private readonly IDictionaryDefinitionParser _parser;
        private readonly SqlParsedDefinitionWriter _parsedWriter;
        private readonly SqlDictionaryEntryCrossReferenceWriter _crossRefWriter;
        private readonly SqlDictionaryAliasWriter _aliasWriter;
        private readonly IEntryEtymologyWriter _etymologyWriter;
        private readonly SqlDictionaryEntryVariantWriter _variantWriter;
        private readonly IDictionaryEntryExampleWriter _exampleWriter; // NEW
        private readonly ILogger<DictionaryParsedDefinitionProcessor> _logger;

        public DictionaryParsedDefinitionProcessor(
            string connectionString,
            IDictionaryDefinitionParser parser,
            SqlParsedDefinitionWriter parsedWriter,
            SqlDictionaryEntryCrossReferenceWriter crossRefWriter,
            SqlDictionaryAliasWriter aliasWriter,
            IEntryEtymologyWriter etymologyWriter,
            SqlDictionaryEntryVariantWriter variantWriter,
            IDictionaryEntryExampleWriter exampleWriter, // NEW
            ILogger<DictionaryParsedDefinitionProcessor> logger)
        {
            _connectionString = connectionString;
            _parser = parser;
            _parsedWriter = parsedWriter;
            _crossRefWriter = crossRefWriter;
            _aliasWriter = aliasWriter;
            _etymologyWriter = etymologyWriter;
            _variantWriter = variantWriter;
            _exampleWriter = exampleWriter; // NEW
            _logger = logger;
        }

        public async Task ExecuteAsync(
            string sourceCode,
            CancellationToken ct)
        {
            _logger.LogInformation(
                "Stage=Parsing started | Source={SourceCode}",
                sourceCode);

            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            // --------------------------------------------------
            // 1. Load ALL DictionaryEntry rows for source
            // --------------------------------------------------
            var entries =
                (await conn.QueryAsync<DictionaryEntry>(
                    """
                    SELECT *
                    FROM dbo.DictionaryEntry
                    WHERE SourceCode = @SourceCode
                    """,
                    new { SourceCode = sourceCode }))
                .ToList();

            _logger.LogInformation(
                "Stage=Parsing | EntriesLoaded={Count} | Source={SourceCode}",
                entries.Count,
                sourceCode);

            int entryIndex = 0;
            int parsedInserted = 0;
            int crossRefInserted = 0;
            int aliasInserted = 0;
            int variantInserted = 0;
            int exampleInserted = 0; // NEW

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                entryIndex++;

                if (entryIndex % 1_000 == 0)
                {
                    _logger.LogInformation(
                        "Stage=Parsing progress | Source={SourceCode} | Entries={Processed}/{Total} | ParsedInserted={Parsed} | ExamplesInserted={Examples}", // UPDATED
                        sourceCode,
                        entryIndex,
                        entries.Count,
                        parsedInserted,
                        exampleInserted); // NEW
                }

                // --------------------------------------------------
                // 2. Parse entry
                // --------------------------------------------------
                var parsedDefinitions =
                    _parser.Parse(entry)?.ToList();

                if (parsedDefinitions == null || parsedDefinitions.Count == 0)
                {
                    parsedDefinitions = new List<ParsedDefinition>
                    {
                        new ParsedDefinition
                        {
                            Definition = null,
                            RawFragment = entry.Definition,
                            SenseNumber = entry.SenseNumber
                        }
                    };
                }

                if (parsedDefinitions.Count != 1)
                {
                    throw new InvalidOperationException(
                        $"Parser returned {parsedDefinitions.Count} ParsedDefinitions for DictionaryEntryId={entry.DictionaryEntryId}. Exactly 1 is required.");
                }

                foreach (var parsed in parsedDefinitions)
                {
                    // --------------------------------------------------
                    // 3. Persist parsed definition
                    // --------------------------------------------------
                    var parsedId =
                        await _parsedWriter.WriteAsync(
                            entry.DictionaryEntryId,
                            parsed,
                            parentParsedId: null,
                            ct);

                    if (parsedId <= 0)
                    {
                        throw new InvalidOperationException(
                            $"ParsedDefinition insert failed for DictionaryEntryId={entry.DictionaryEntryId}");
                    }

                    parsedInserted++;

                    // --------------------------------------------------
                    // 4. Extract and save examples (NEW)
                    // --------------------------------------------------
                    var examples = ExtractExamplesFromDefinition(parsed.Definition, sourceCode);
                    foreach (var example in examples)
                    {
                        await _exampleWriter.WriteAsync(
                            parsedId,
                            example,
                            sourceCode,
                            ct);

                        exampleInserted++;
                    }

                    // --------------------------------------------------
                    // 5. Cross-references
                    // --------------------------------------------------
                    foreach (var cr in parsed.CrossReferences)
                    {
                        await _crossRefWriter.WriteAsync(
                            parsedId,
                            cr,
                            ct);

                        crossRefInserted++;
                    }

                    // --------------------------------------------------
                    // 6. Alias
                    // --------------------------------------------------
                    if (!string.IsNullOrWhiteSpace(parsed.Alias))
                    {
                        await _aliasWriter.WriteAsync(
                            parsedId,
                            parsed.Alias,
                            ct);

                        aliasInserted++;
                    }
                }

                // --------------------------------------------------
                // 7. Entry-level etymology
                // --------------------------------------------------
                if (!string.IsNullOrWhiteSpace(entry.Etymology))
                {
                    await _etymologyWriter.WriteAsync(
                        new DictionaryEntryEtymology
                        {
                            DictionaryEntryId = entry.DictionaryEntryId,
                            EtymologyText = entry.Etymology,
                            CreatedUtc = DateTime.UtcNow
                        },
                        ct);
                }

                // --------------------------------------------------
                // 8. Headword variants
                // --------------------------------------------------
                foreach (var (variant, type)
                    in Sources.Gutenberg.Parsing.WebsterHeadwordVariantGenerator
                        .Generate(entry.Word))
                {
                    await _variantWriter.WriteAsync(
                        entry.DictionaryEntryId,
                        variant,
                        type,
                        ct);

                    variantInserted++;
                }
            }

            _logger.LogInformation(
                "Stage=Parsing completed | Source={SourceCode} | Entries={Entries} | ParsedInserted={Parsed} | CrossRefs={CrossRefs} | Aliases={Aliases} | Variants={Variants} | Examples={Examples}", // UPDATED
                sourceCode,
                entryIndex,
                parsedInserted,
                crossRefInserted,
                aliasInserted,
                variantInserted,
                exampleInserted); // NEW
        }
        private static IReadOnlyList<string> ExtractExamplesFromDefinition(string? definition, string sourceCode)
        {
            var examples = new List<string>();

            if (string.IsNullOrWhiteSpace(definition))
                return examples;

            switch (sourceCode)
            {
                case "GUT_WEBSTER":
                    examples.AddRange(ExtractWebsterExamples(definition));
                    break;
                case "ENG_CHN":
                    examples.AddRange(ExtractEnglishChineseExamples(definition));
                    break;
                case "STRUCT_JSON":
                    examples.AddRange(ExtractStructuredJsonExamples(definition));
                    break;
                case "ENG_COLLINS":
                    examples.AddRange(CollinsParserHelper.ExtractExamples(definition).ToList());
                    break;
                default:
                    examples.AddRange(ExtractGenericExamples(definition));
                    break;
            }
            return examples.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).Distinct().ToList();
        }
        private static IEnumerable<string> ExtractWebsterExamples(string definition)
        {
            // Webster examples are often in quotes or after "e.g."
            var examples = new List<string>();

            // Extract quoted examples
            var quotedMatches = System.Text.RegularExpressions.Regex.Matches(
                definition,
                @"[""']([^""']+)[""']");

            foreach (System.Text.RegularExpressions.Match match in quotedMatches)
            {
                examples.Add(match.Groups[1].Value);
            }

            // Extract examples after "e.g." or "for example"
            var egMatches = System.Text.RegularExpressions.Regex.Matches(
                definition,
                @"(?:e\.g\.|for example|ex\.|example:)\s*([^.;]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            foreach (System.Text.RegularExpressions.Match match in egMatches)
            {
                examples.Add(match.Groups[1].Value.Trim());
            }

            return examples;
        }
        private static IEnumerable<string> ExtractEnglishChineseExamples(string definition)
        {
            // English-Chinese examples might have Chinese text
            // Could look for patterns like "e.g." or example sentences
            var examples = new List<string>();

            // Look for Chinese example markers
            var chineseMarkers = new[] { "例如", "比如", "例句", "例子" };

            foreach (var marker in chineseMarkers)
            {
                if (definition.Contains(marker))
                {
                    // Simple extraction - get text after marker
                    var index = definition.IndexOf(marker);
                    if (index >= 0)
                    {
                        var example = definition.Substring(index + marker.Length).Trim();
                        // Take until next punctuation or reasonable length
                        var endChars = new[] { '。', '.', ';', '，', ',' };
                        var endIndex = example.IndexOfAny(endChars);
                        if (endIndex > 0)
                        {
                            example = example.Substring(0, endIndex);
                        }
                        examples.Add(example.Trim());
                    }
                }
            }

            return examples;
        }
        private static IEnumerable<string> ExtractStructuredJsonExamples(string definition)
        {
            // Structured JSON might have examples in specific formats
            var examples = new List<string>();

            // Look for example markers
            var exampleMarkers = new[] { "example:", "eg:", "e.g.:", "for instance:" };

            foreach (var marker in exampleMarkers)
            {
                if (definition.ToLower().Contains(marker))
                {
                    var index = definition.ToLower().IndexOf(marker);
                    if (index >= 0)
                    {
                        var example = definition.Substring(index + marker.Length).Trim();
                        // Take first sentence
                        var endIndex = example.IndexOfAny(new[] { '.', ';', '!' });
                        if (endIndex > 0)
                        {
                            example = example.Substring(0, endIndex + 1);
                        }
                        examples.Add(example.Trim());
                    }
                }
            }

            return examples;
        }
        private static IEnumerable<string> ExtractGenericExamples(string definition)
        {
            // Generic example extraction for any source
            var examples = new List<string>();

            // Look for quoted text
            var quotedMatches = System.Text.RegularExpressions.Regex.Matches(
                definition,
                @"[""']([^""']+)[""']");

            foreach (System.Text.RegularExpressions.Match match in quotedMatches)
            {
                // Only consider quotes that look like examples (not too short)
                if (match.Groups[1].Value.Length > 10)
                {
                    examples.Add(match.Groups[1].Value);
                }
            }

            return examples;
        }
    }
}