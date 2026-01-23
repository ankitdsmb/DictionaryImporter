using System.Security.Cryptography;
using DictionaryImporter.Common;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlParsedDefinitionWriter(
        string connectionString,
        GenericSqlBatcher batcher,
        ILogger<SqlParsedDefinitionWriter> logger)
    {
        public async Task<long> WriteAsync(
            long dictionaryEntryId,
            ParsedDefinition parsed,
            string sourceCode,
            CancellationToken ct)
        {
            sourceCode = string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode;

            if (dictionaryEntryId <= 0 || parsed == null)
                return 0;

            // ✅ KAIKKI: prevent duplicates globally (ignore DictionaryEntryId)
            if (ShouldPreventDuplicates(sourceCode))
            {
                var existingId = await TryFindExistingParsedIdAsync(dictionaryEntryId, parsed, sourceCode, ct);
                if (existingId.HasValue)
                {
                    logger.LogDebug(
                        "Duplicate parsed definition skipped. DictionaryEntryId={DictionaryEntryId}, Source={Source}, ExistingParsedId={ExistingParsedId}",
                        dictionaryEntryId, sourceCode, existingId.Value);

                    return existingId.Value;
                }
            }

            bool hasNonEnglishText = Helper.LanguageDetector.ContainsNonEnglishText(parsed.Definition ?? "");
            long? nonEnglishTextId = null;

            string definitionToStore = parsed.Definition ?? "";

            if (hasNonEnglishText)
            {
                logger.LogDebug(
                    "Non-English text detected for DictionaryEntryId={DictionaryEntryId}, Source={Source}",
                    dictionaryEntryId, sourceCode);

                nonEnglishTextId = await StoreNonEnglishTextAsync(
                    definitionToStore,
                    sourceCode,
                    fieldType: "Definition",
                    ct);
            }

            // FIX:
            // If Kaikki => do global NOT EXISTS ignoring DictionaryEntryId
            // Else => insert normally (existing behavior)
            var sql = ShouldPreventDuplicates(sourceCode)
                ? """
                  BEGIN TRY

                      IF NOT EXISTS (
                          SELECT 1
                          FROM dbo.DictionaryEntryParsed WITH (UPDLOCK, HOLDLOCK)
                          WHERE SourceCode = @SourceCode
                            AND SenseNumber = @SenseNumber
                            AND CONVERT(varchar(64), HASHBYTES('SHA2_256', LOWER(LTRIM(RTRIM(ISNULL(MeaningTitle, ''))))), 2) =
                                CONVERT(varchar(64), HASHBYTES('SHA2_256', LOWER(LTRIM(RTRIM(ISNULL(@MeaningTitle, ''))))), 2)
                            AND CONVERT(varchar(64), HASHBYTES('SHA2_256', LOWER(LTRIM(RTRIM(ISNULL(Definition, ''))))), 2) =
                                CONVERT(varchar(64), HASHBYTES('SHA2_256', LOWER(LTRIM(RTRIM(ISNULL(@Definition, ''))))), 2)
                            AND ISNULL(Domain, '') = ISNULL(@Domain, '')
                            AND ISNULL(UsageLabel, '') = ISNULL(@UsageLabel, '')
                      )
                      BEGIN
                          INSERT INTO dbo.DictionaryEntryParsed (
                              DictionaryEntryId, ParentParsedId, MeaningTitle,
                              Definition, RawFragment, SenseNumber,
                              Domain, UsageLabel, HasNonEnglishText, NonEnglishTextId, SourceCode,
                              CreatedUtc
                          ) VALUES (
                              @DictionaryEntryId, @ParentParsedId, @MeaningTitle,
                              @Definition, @RawFragment, @SenseNumber,
                              @Domain, @UsageLabel, @HasNonEnglishText, @NonEnglishTextId, @SourceCode,
                              SYSUTCDATETIME()
                          );

                          SELECT CAST(SCOPE_IDENTITY() AS BIGINT);
                          RETURN;
                      END

                      -- Return existing
                      SELECT TOP (1) DictionaryEntryParsedId
                      FROM dbo.DictionaryEntryParsed WITH (NOLOCK)
                      WHERE SourceCode = @SourceCode
                        AND SenseNumber = @SenseNumber
                        AND CONVERT(varchar(64), HASHBYTES('SHA2_256', LOWER(LTRIM(RTRIM(ISNULL(MeaningTitle, ''))))), 2) =
                            CONVERT(varchar(64), HASHBYTES('SHA2_256', LOWER(LTRIM(RTRIM(ISNULL(@MeaningTitle, ''))))), 2)
                        AND CONVERT(varchar(64), HASHBYTES('SHA2_256', LOWER(LTRIM(RTRIM(ISNULL(Definition, ''))))), 2) =
                            CONVERT(varchar(64), HASHBYTES('SHA2_256', LOWER(LTRIM(RTRIM(ISNULL(@Definition, ''))))), 2)
                        AND ISNULL(Domain, '') = ISNULL(@Domain, '')
                        AND ISNULL(UsageLabel, '') = ISNULL(@UsageLabel, '')
                      ORDER BY DictionaryEntryParsedId DESC;

                  END TRY
                  BEGIN CATCH

                      DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();
                      DECLARE @ErrSeverity INT = ERROR_SEVERITY();
                      DECLARE @ErrState INT = ERROR_STATE();

                      RAISERROR(@ErrMsg, @ErrSeverity, @ErrState);

                  END CATCH
                  """
                : """
                  BEGIN TRY

                      INSERT INTO dbo.DictionaryEntryParsed (
                          DictionaryEntryId, ParentParsedId, MeaningTitle,
                          Definition, RawFragment, SenseNumber,
                          Domain, UsageLabel, HasNonEnglishText, NonEnglishTextId, SourceCode,
                          CreatedUtc
                      ) VALUES (
                          @DictionaryEntryId, @ParentParsedId, @MeaningTitle,
                          @Definition, @RawFragment, @SenseNumber,
                          @Domain, @UsageLabel, @HasNonEnglishText, @NonEnglishTextId, @SourceCode,
                          SYSUTCDATETIME()
                      );

                      SELECT CAST(SCOPE_IDENTITY() AS BIGINT);

                  END TRY
                  BEGIN CATCH

                      IF ERROR_NUMBER() IN (2601, 2627)
                      BEGIN
                          SELECT TOP 1 DictionaryEntryParsedId
                          FROM dbo.DictionaryEntryParsed
                          WHERE DictionaryEntryId = @DictionaryEntryId
                            AND SourceCode = @SourceCode
                            AND SenseNumber = @SenseNumber
                          ORDER BY DictionaryEntryParsedId DESC;

                          RETURN;
                      END

                      DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();
                      DECLARE @ErrSeverity INT = ERROR_SEVERITY();
                      DECLARE @ErrState INT = ERROR_STATE();

                      RAISERROR(@ErrMsg, @ErrSeverity, @ErrState);

                  END CATCH
                  """;

            var parameters = new
            {
                DictionaryEntryId = dictionaryEntryId,
                ParentParsedId = parsed.ParentParsedId,
                MeaningTitle = parsed.MeaningTitle ?? "",
                Definition = definitionToStore,
                RawFragment = parsed.RawFragment ?? "",
                SenseNumber = parsed.SenseNumber,
                Domain = parsed.Domain,
                UsageLabel = parsed.UsageLabel,
                HasNonEnglishText = hasNonEnglishText,
                NonEnglishTextId = nonEnglishTextId,
                SourceCode = sourceCode
            };

            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);

            try
            {
                var parsedId = await conn.ExecuteScalarAsync<long>(
                    new CommandDefinition(sql, parameters, cancellationToken: ct));

                return parsedId;
            }
            catch (SqlException ex) when (IsUniqueConstraintViolation(ex))
            {
                var existingId = await TryFindExistingParsedIdAsync(dictionaryEntryId, parsed, sourceCode, ct);
                if (existingId.HasValue)
                {
                    logger.LogDebug(
                        "Duplicate parsed definition prevented by DB constraint. DictionaryEntryId={DictionaryEntryId}, Source={Source}, ExistingParsedId={ExistingParsedId}",
                        dictionaryEntryId, sourceCode, existingId.Value);

                    return existingId.Value;
                }

                logger.LogWarning(
                    ex,
                    "Unique constraint violation occurred but existing parsed row could not be resolved. DictionaryEntryId={DictionaryEntryId}, Source={Source}",
                    dictionaryEntryId, sourceCode);

                return 0;
            }
        }

        public async Task WriteBatchAsync(
            IEnumerable<(long DictionaryEntryId, ParsedDefinition Parsed, string SourceCode)> entries,
            CancellationToken ct)
        {
            var batchEntries = new List<object>();

            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();

                if (entry.DictionaryEntryId <= 0 || entry.Parsed == null)
                    continue;

                var safeSourceCode = string.IsNullOrWhiteSpace(entry.SourceCode) ? "UNKNOWN" : entry.SourceCode;

                bool hasNonEnglishText = Helper.LanguageDetector.ContainsNonEnglishText(entry.Parsed.Definition ?? "");
                string definitionToStore = entry.Parsed.Definition ?? "";
                long? nonEnglishTextId = null;

                batchEntries.Add(new
                {
                    DictionaryEntryId = entry.DictionaryEntryId,
                    ParentParsedId = entry.Parsed.ParentParsedId,
                    MeaningTitle = entry.Parsed.MeaningTitle ?? "",
                    Definition = definitionToStore,
                    RawFragment = entry.Parsed.RawFragment ?? "",
                    SenseNumber = entry.Parsed.SenseNumber,
                    Domain = entry.Parsed.Domain,
                    UsageLabel = entry.Parsed.UsageLabel,
                    Alias = entry.Parsed.Alias,
                    HasNonEnglishText = hasNonEnglishText,
                    NonEnglishTextId = nonEnglishTextId,
                    SourceCode = safeSourceCode
                });
            }

            if (batchEntries.Count == 0)
                return;

            // FIX:
            // Kaikki batch => de-dupe WITHOUT DictionaryEntryId
            // Other sources => de-dupe WITH DictionaryEntryId (existing safer behavior)
            var batchSql = """
                INSERT INTO dbo.DictionaryEntryParsed (
                    DictionaryEntryId, ParentParsedId, MeaningTitle,
                    Definition, RawFragment, SenseNumber,
                    Domain, UsageLabel, Alias,
                    HasNonEnglishText, NonEnglishTextId, SourceCode,
                    CreatedUtc
                )
                SELECT
                    @DictionaryEntryId, @ParentParsedId, @MeaningTitle,
                    @Definition, @RawFragment, @SenseNumber,
                    @Domain, @UsageLabel, @Alias,
                    @HasNonEnglishText, @NonEnglishTextId, @SourceCode,
                    SYSUTCDATETIME()
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM dbo.DictionaryEntryParsed WITH (UPDLOCK, HOLDLOCK)
                    WHERE
                        (
                            @SourceCode = 'KAIKKI'
                            AND SourceCode = @SourceCode
                            AND SenseNumber = @SenseNumber
                            AND CONVERT(varchar(64), HASHBYTES('SHA2_256', LOWER(LTRIM(RTRIM(ISNULL(MeaningTitle, ''))))), 2) =
                                CONVERT(varchar(64), HASHBYTES('SHA2_256', LOWER(LTRIM(RTRIM(ISNULL(@MeaningTitle, ''))))), 2)
                            AND CONVERT(varchar(64), HASHBYTES('SHA2_256', LOWER(LTRIM(RTRIM(ISNULL(Definition, ''))))), 2) =
                                CONVERT(varchar(64), HASHBYTES('SHA2_256', LOWER(LTRIM(RTRIM(ISNULL(@Definition, ''))))), 2)
                            AND ISNULL(Domain, '') = ISNULL(@Domain, '')
                            AND ISNULL(UsageLabel, '') = ISNULL(@UsageLabel, '')
                        )
                        OR
                        (
                            @SourceCode <> 'KAIKKI'
                            AND DictionaryEntryId = @DictionaryEntryId
                            AND SourceCode = @SourceCode
                            AND SenseNumber = @SenseNumber
                            AND CONVERT(varchar(64), HASHBYTES('SHA2_256', LOWER(LTRIM(RTRIM(ISNULL(MeaningTitle, ''))))), 2) =
                                CONVERT(varchar(64), HASHBYTES('SHA2_256', LOWER(LTRIM(RTRIM(ISNULL(@MeaningTitle, ''))))), 2)
                            AND CONVERT(varchar(64), HASHBYTES('SHA2_256', LOWER(LTRIM(RTRIM(ISNULL(Definition, ''))))), 2) =
                                CONVERT(varchar(64), HASHBYTES('SHA2_256', LOWER(LTRIM(RTRIM(ISNULL(@Definition, ''))))), 2)
                            AND ISNULL(Domain, '') = ISNULL(@Domain, '')
                            AND ISNULL(UsageLabel, '') = ISNULL(@UsageLabel, '')
                        )
                );
                """;

            await batcher.QueueOperationAsync(
                "BATCH_INSERT_ParsedDefinition",
                batchSql,
                batchEntries,
                CommandType.Text,
                30);

            logger.LogInformation(
                "Queued batch of {Count} parsed definitions for insertion",
                batchEntries.Count);
        }

        private async Task<long> StoreNonEnglishTextAsync(
            string originalText,
            string sourceCode,
            string fieldType,
            CancellationToken ct)
        {
            const string sql = """
                               INSERT INTO dbo.DictionaryNonEnglishText (
                                   OriginalText, DetectedLanguage, CharacterCount,
                                   SourceCode, FieldType, CreatedUtc
                               ) OUTPUT INSERTED.NonEnglishTextId
                               VALUES (
                                   @OriginalText, @DetectedLanguage, @CharacterCount,
                                   @SourceCode, @FieldType, SYSUTCDATETIME()
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

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);

            return await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(sql, parameters, cancellationToken: ct));
        }

        private static bool ShouldPreventDuplicates(string sourceCode)
        {
            if (string.IsNullOrWhiteSpace(sourceCode))
                return false;

            return sourceCode.Equals("KAIKKI", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<long?> TryFindExistingParsedIdAsync(
            long dictionaryEntryId,
            ParsedDefinition parsed,
            string sourceCode,
            CancellationToken ct)
        {
            try
            {
                var meaningTitle = parsed.MeaningTitle ?? "";
                var definition = parsed.Definition ?? "";
                var senseNumber = parsed.SenseNumber;
                var domain = parsed.Domain;
                var usageLabel = parsed.UsageLabel;

                var definitionHash = ComputeSha256Hex(NormalizeForHash(definition));
                var meaningHash = ComputeSha256Hex(NormalizeForHash(meaningTitle));

                // FIX:
                // Kaikki => ignore DictionaryEntryId in lookup
                // Other sources => include DictionaryEntryId
                var sql = ShouldPreventDuplicates(sourceCode)
                    ? """
                      SELECT TOP (1) DictionaryEntryParsedId
                      FROM dbo.DictionaryEntryParsed WITH (NOLOCK)
                      WHERE SourceCode = @SourceCode
                        AND SenseNumber = @SenseNumber
                        AND CONVERT(varchar(64), HASHBYTES('SHA2_256', LOWER(LTRIM(RTRIM(ISNULL(MeaningTitle, ''))))), 2) = @MeaningHash
                        AND CONVERT(varchar(64), HASHBYTES('SHA2_256', LOWER(LTRIM(RTRIM(ISNULL(Definition, ''))))), 2) = @DefinitionHash
                        AND ISNULL(Domain,'') = ISNULL(@Domain,'')
                        AND ISNULL(UsageLabel,'') = ISNULL(@UsageLabel,'')
                      ORDER BY DictionaryEntryParsedId DESC;
                      """
                    : """
                      SELECT TOP (1) DictionaryEntryParsedId
                      FROM dbo.DictionaryEntryParsed WITH (NOLOCK)
                      WHERE DictionaryEntryId = @DictionaryEntryId
                        AND SourceCode = @SourceCode
                        AND SenseNumber = @SenseNumber
                        AND CONVERT(varchar(64), HASHBYTES('SHA2_256', LOWER(LTRIM(RTRIM(ISNULL(MeaningTitle, ''))))), 2) = @MeaningHash
                        AND CONVERT(varchar(64), HASHBYTES('SHA2_256', LOWER(LTRIM(RTRIM(ISNULL(Definition, ''))))), 2) = @DefinitionHash
                        AND ISNULL(Domain,'') = ISNULL(@Domain,'')
                        AND ISNULL(UsageLabel,'') = ISNULL(@UsageLabel,'')
                      ORDER BY DictionaryEntryParsedId DESC;
                      """;

                await using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync(ct);

                var id = await conn.ExecuteScalarAsync<long?>(
                    new CommandDefinition(
                        sql,
                        new
                        {
                            DictionaryEntryId = dictionaryEntryId,
                            SourceCode = string.IsNullOrWhiteSpace(sourceCode) ? "UNKNOWN" : sourceCode,
                            SenseNumber = senseNumber,
                            MeaningHash = meaningHash,
                            DefinitionHash = definitionHash,
                            Domain = domain,
                            UsageLabel = usageLabel
                        },
                        cancellationToken: ct));

                return id;
            }
            catch (Exception ex)
            {
                logger.LogDebug(
                    ex,
                    "Failed to check duplicate parsed definition. DictionaryEntryId={DictionaryEntryId}, Source={Source}",
                    dictionaryEntryId, sourceCode);

                return null;
            }
        }

        private static bool IsUniqueConstraintViolation(SqlException ex)
        {
            return ex.Number == 2601 || ex.Number == 2627;
        }

        private static string NormalizeForHash(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var t = text.Trim().ToLowerInvariant();
            t = Regex.Replace(t, @"\s+", " ");
            t = t.Replace("’", "'");

            if (t.Length > 4000)
                t = t.Substring(0, 4000);

            return t;
        }

        private static string ComputeSha256Hex(string input)
        {
            input ??= string.Empty;

            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = SHA256.HashData(bytes);

            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
                sb.Append(b.ToString("x2"));

            return sb.ToString();
        }
    }
}
