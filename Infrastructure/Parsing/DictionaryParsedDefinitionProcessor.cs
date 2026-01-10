using Dapper;
using DictionaryImporter.Core.Abstractions;
using DictionaryImporter.Core.Parsing;
using DictionaryImporter.Domain.Models;
using DictionaryImporter.Infrastructure.Persistence;
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
        private readonly ILogger<DictionaryParsedDefinitionProcessor> _logger;

        public DictionaryParsedDefinitionProcessor(
            string connectionString,
            IDictionaryDefinitionParser parser,
            SqlParsedDefinitionWriter parsedWriter,
            SqlDictionaryEntryCrossReferenceWriter crossRefWriter,
            SqlDictionaryAliasWriter aliasWriter,
            IEntryEtymologyWriter etymologyWriter,
            SqlDictionaryEntryVariantWriter variantWriter,
            ILogger<DictionaryParsedDefinitionProcessor> logger)
        {
            _connectionString = connectionString;
            _parser = parser;
            _parsedWriter = parsedWriter;
            _crossRefWriter = crossRefWriter;
            _aliasWriter = aliasWriter;
            _etymologyWriter = etymologyWriter;
            _variantWriter = variantWriter;
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

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                entryIndex++;

                if (entryIndex % 1_000 == 0)
                {
                    _logger.LogInformation(
                        "Stage=Parsing progress | Source={SourceCode} | Entries={Processed}/{Total} | ParsedInserted={Parsed}",
                        sourceCode,
                        entryIndex,
                        entries.Count,
                        parsedInserted);
                }

                // --------------------------------------------------
                // 2. Parse entry (MUST return exactly one result)
                // --------------------------------------------------
                var parsedDefinitions =
                    _parser.Parse(entry)?.ToList();

                if (parsedDefinitions == null || parsedDefinitions.Count == 0)
                {
                    // ENFORCE 1:1 CONTRACT
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
                    // 3. Persist parsed definition (IDEMPOTENT)
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
                    // 4. Cross-references
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
                    // 5. Alias
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
                // 6. Entry-level etymology (WRITE-ONCE)
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
                // 7. Headword variants (ENTRY-LEVEL, IDEMPOTENT)
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
                "Stage=Parsing completed | Source={SourceCode} | Entries={Entries} | ParsedInserted={Parsed} | CrossRefs={CrossRefs} | Aliases={Aliases} | Variants={Variants}",
                sourceCode,
                entryIndex,
                parsedInserted,
                crossRefInserted,
                aliasInserted,
                variantInserted);
        }
    }
}
