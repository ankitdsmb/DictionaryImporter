using DictionaryImporter.Common;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlDictionaryEntrySynonymWriter : IDictionaryEntrySynonymWriter, IDisposable
    {
        private readonly string _connectionString;
        private readonly ILogger<SqlDictionaryEntrySynonymWriter> _logger;
        private readonly GenericSqlBatcher _batcher;
        private readonly bool _ownsBatcher;

        public SqlDictionaryEntrySynonymWriter(
            string connectionString,
            ILogger<SqlDictionaryEntrySynonymWriter> logger,
            GenericSqlBatcher batcher)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _batcher = batcher ?? throw new ArgumentNullException(nameof(batcher));
            _ownsBatcher = false;

            _logger.LogInformation("SqlDictionaryEntrySynonymWriter initialized with injected batcher");
        }

        public SqlDictionaryEntrySynonymWriter(
            string connectionString,
            ILogger<SqlDictionaryEntrySynonymWriter> logger)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _batcher = CreateInternalBatcher();
            _ownsBatcher = true;

            _logger.LogInformation("SqlDictionaryEntrySynonymWriter created internal batcher");
        }

        private GenericSqlBatcher CreateInternalBatcher()
        {
            try
            {
                var nullLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<GenericSqlBatcher>.Instance;
                return new GenericSqlBatcher(_connectionString, nullLogger);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create internal batcher");
                throw;
            }
        }

        public async Task WriteAsync(DictionaryEntrySynonym synonym, CancellationToken ct)
        {
            if (synonym == null)
                throw new ArgumentNullException(nameof(synonym));

            synonym.SourceCode = string.IsNullOrWhiteSpace(synonym.SourceCode) ? "UNKNOWN" : synonym.SourceCode.Trim();

            if (synonym.DictionaryEntryParsedId <= 0)
                return;

            var rawSynonymText = synonym.SynonymText ?? string.Empty;
            rawSynonymText = rawSynonymText.Trim();

            if (string.IsNullOrWhiteSpace(rawSynonymText))
                return;

            // Detect non-English synonym
            bool hasNonEnglishText = Helper.LanguageDetector.ContainsNonEnglishText(rawSynonymText);
            long? nonEnglishTextId = null;

            string? synonymToStore = rawSynonymText;

            if (hasNonEnglishText)
            {
                nonEnglishTextId = await StoreNonEnglishTextAsync(
                    originalText: rawSynonymText,
                    sourceCode: synonym.SourceCode,
                    fieldType: "Synonym",
                    ct);

                // Store SynonymText as NULL and link via NonEnglishTextId
                synonymToStore = null;
            }
            else
            {
                synonymToStore = Helper.NormalizeSynonymText(synonymToStore);

                if (string.IsNullOrWhiteSpace(synonymToStore))
                    return;
            }

            // Concurrency-safe dedupe (NOLOCK removed)
            const string sql = """
                IF NOT EXISTS (
                    SELECT 1
                    FROM dbo.DictionaryEntrySynonym WITH (UPDLOCK, HOLDLOCK)
                    WHERE DictionaryEntryParsedId = @DictionaryEntryParsedId
                      AND SourceCode = @SourceCode
                      AND (
                            (
                                @HasNonEnglishText = 0
                                AND SynonymText = @SynonymText
                                AND NonEnglishTextId IS NULL
                            )
                            OR
                            (
                                @HasNonEnglishText = 1
                                AND NonEnglishTextId = @NonEnglishTextId
                            )
                          )
                )
                BEGIN
                    INSERT INTO dbo.DictionaryEntrySynonym
                    (DictionaryEntryParsedId, SynonymText, SourceCode, CreatedUtc, HasNonEnglishText, NonEnglishTextId)
                    VALUES
                    (@DictionaryEntryParsedId, @SynonymText, @SourceCode, SYSUTCDATETIME(), @HasNonEnglishText, @NonEnglishTextId);
                END
                """;

            var parameters = new
            {
                DictionaryEntryParsedId = synonym.DictionaryEntryParsedId,
                SynonymText = synonymToStore,
                SourceCode = synonym.SourceCode,
                HasNonEnglishText = hasNonEnglishText,
                NonEnglishTextId = nonEnglishTextId
            };

            await _batcher.QueueOperationAsync(
                "INSERT_Synonym",
                sql,
                parameters,
                CommandType.Text,
                30,
                ct);
        }

        public async Task BulkWriteAsync(
            IEnumerable<DictionaryEntrySynonym> synonyms,
            CancellationToken ct)
        {
            // Keep bulk write ONLY for English normalized synonyms
            if (synonyms == null)
                return;

            var synonymTable = CreateSynonymTable(synonyms);

            if (synonymTable.Rows.Count == 0)
                return;

            const string sql = @"
                INSERT INTO dbo.DictionaryEntrySynonym
                (DictionaryEntryParsedId, SynonymText, SourceCode, CreatedUtc, HasNonEnglishText, NonEnglishTextId)
                SELECT
                    s.DictionaryEntryParsedId,
                    s.SynonymText,
                    ISNULL(s.SourceCode, 'UNKNOWN'),
                    SYSUTCDATETIME(),
                    0,
                    NULL
                FROM @Synonyms s
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM dbo.DictionaryEntrySynonym es WITH (UPDLOCK, HOLDLOCK)
                    WHERE es.DictionaryEntryParsedId = s.DictionaryEntryParsedId
                      AND es.SynonymText = s.SynonymText
                      AND es.SourceCode = ISNULL(s.SourceCode, 'UNKNOWN')
                      AND es.NonEnglishTextId IS NULL
                )";

            var param = new
            {
                Synonyms = synonymTable.AsTableValuedParameter("dbo.DictionaryEntrySynonymType")
            };

            await _batcher.ExecuteImmediateAsync(sql, param, CommandType.Text, 30, ct);
        }

        public async Task WriteSynonymsForParsedDefinition(
            long parsedDefinitionId,
            IEnumerable<string> synonyms,
            string sourceCode,
            CancellationToken ct)
        {
            if (parsedDefinitionId <= 0)
                return;

            var synonymList = synonyms?.ToList() ?? new List<string>();
            if (synonymList.Count == 0)
                return;

            sourceCode = string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode.Trim();

            // IMPORTANT:
            // Split English vs non-English so bulk path stays safe/fast
            var englishSynonyms = new List<string>();
            var nonEnglishSynonyms = new List<string>();

            foreach (var s in synonymList)
            {
                var raw = (s ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                if (Helper.LanguageDetector.ContainsNonEnglishText(raw))
                    nonEnglishSynonyms.Add(raw);
                else
                    englishSynonyms.Add(raw);
            }

            // 1) English synonyms: bulk insert
            var uniqueEnglish = englishSynonyms
                .Select(Helper.NormalizeSynonymText)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (uniqueEnglish.Count > 0)
            {
                _logger.LogDebug(
                    "Writing {Count} English synonyms for parsed definition {ParsedId} | SourceCode={SourceCode}",
                    uniqueEnglish.Count,
                    parsedDefinitionId,
                    sourceCode);

                var synonymObjects = uniqueEnglish.Select(s => new DictionaryEntrySynonym
                {
                    DictionaryEntryParsedId = parsedDefinitionId,
                    SynonymText = s,
                    SourceCode = sourceCode,
                    CreatedUtc = DateTime.UtcNow
                });

                await BulkWriteAsync(synonymObjects, ct);
            }

            // 2) Non-English synonyms: insert individually with NonEnglishTextId
            var uniqueNonEnglish = nonEnglishSynonyms
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (uniqueNonEnglish.Count > 0)
            {
                _logger.LogDebug(
                    "Writing {Count} non-English synonyms for parsed definition {ParsedId} | SourceCode={SourceCode}",
                    uniqueNonEnglish.Count,
                    parsedDefinitionId,
                    sourceCode);

                foreach (var nonEng in uniqueNonEnglish)
                {
                    var obj = new DictionaryEntrySynonym
                    {
                        DictionaryEntryParsedId = parsedDefinitionId,
                        SynonymText = nonEng,
                        SourceCode = sourceCode,
                        CreatedUtc = DateTime.UtcNow
                    };

                    await WriteAsync(obj, ct);
                }
            }
        }

        private static DataTable CreateSynonymTable(IEnumerable<DictionaryEntrySynonym> synonyms)
        {
            var table = new DataTable();
            table.Columns.Add("DictionaryEntryParsedId", typeof(long));
            table.Columns.Add("SynonymText", typeof(string));
            table.Columns.Add("SourceCode", typeof(string));

            if (synonyms == null)
                return table;

            // IMPORTANT:
            // Table-valued bulk path is ONLY for English synonyms
            var uniqueSynonyms = synonyms
                .Where(s => s != null)
                .Select(s => new DictionaryEntrySynonym
                {
                    DictionaryEntryParsedId = s.DictionaryEntryParsedId,
                    SynonymText = Helper.NormalizeSynonymText(s.SynonymText),
                    SourceCode = string.IsNullOrWhiteSpace(s.SourceCode) ? "UNKNOWN" : s.SourceCode.Trim()
                })
                .Where(s => s.DictionaryEntryParsedId > 0)
                .Where(s => !string.IsNullOrWhiteSpace(s.SynonymText))
                .GroupBy(s => new { s.DictionaryEntryParsedId, s.SynonymText, s.SourceCode })
                .Select(g => g.First())
                .ToList();

            foreach (var synonym in uniqueSynonyms)
            {
                table.Rows.Add(
                    synonym.DictionaryEntryParsedId,
                    synonym.SynonymText,
                    synonym.SourceCode);
            }

            return table;
        }

        private async Task<long> StoreNonEnglishTextAsync(
            string originalText,
            string sourceCode,
            string fieldType,
            CancellationToken ct)
        {
            const string sql = """
                INSERT INTO dbo.DictionaryNonEnglishText (
                    OriginalText,
                    DetectedLanguage,
                    CharacterCount,
                    SourceCode,
                    FieldType,
                    CreatedUtc
                ) OUTPUT INSERTED.NonEnglishTextId
                VALUES (
                    @OriginalText,
                    @DetectedLanguage,
                    @CharacterCount,
                    @SourceCode,
                    @FieldType,
                    SYSUTCDATETIME()
                );
                """;

            var languageCode = Helper.LanguageDetector.DetectLanguageCode(originalText);

            var parameters = new
            {
                OriginalText = originalText,
                DetectedLanguage = languageCode,
                CharacterCount = originalText.Length,
                SourceCode = string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode,
                FieldType = fieldType
            };

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            return await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(sql, parameters, cancellationToken: ct));
        }
        public void Dispose()
        {
            if (_ownsBatcher)
            {
                _batcher?.Dispose();
            }
        }
    }
}
