namespace DictionaryImporter.Infrastructure.Persistence;

/// <summary>
///     Writer for parsed dictionary definitions.
/// </summary>
public sealed class SqlParsedDefinitionWriter(
    string connectionString,
    ILogger<SqlParsedDefinitionWriter> logger)
{
    private const int DomainCodeMaxLength = 50;

    private const int UsageLabelMaxLength = 50;
    private const int MeaningTitleMaxLength = 200;
    private const int CommandTimeoutSeconds = 30;

    private const string MergeSql = """
                                    MERGE dbo.DictionaryEntryParsed AS target
                                    USING
                                    (
                                        SELECT
                                            @DictionaryEntryId AS DictionaryEntryId,
                                            @ParentParsedId AS ParentParsedId,
                                            @MeaningTitle AS MeaningTitle,
                                            @SenseNumber AS SenseNumber
                                    ) AS source
                                    ON target.DictionaryEntryId = source.DictionaryEntryId
                                       AND ISNULL(target.ParentParsedId, -1) = ISNULL(source.ParentParsedId, -1)
                                       AND ISNULL(target.MeaningTitle, '') = ISNULL(source.MeaningTitle, '')
                                       AND ISNULL(target.SenseNumber, -1) = ISNULL(source.SenseNumber, -1)

                                    WHEN NOT MATCHED BY TARGET THEN
                                        INSERT
                                        (
                                            DictionaryEntryId,
                                            ParentParsedId,
                                            MeaningTitle,
                                            SenseNumber,
                                            DomainCode,
                                            UsageLabel,
                                            Definition,
                                            RawFragment,
                                            CreatedUtc
                                        )
                                        VALUES
                                        (
                                            @DictionaryEntryId,
                                            @ParentParsedId,
                                            @MeaningTitle,
                                            @SenseNumber,
                                            @DomainCode,
                                            @UsageLabel,
                                            @Definition,
                                            @RawFragment,
                                            SYSUTCDATETIME()
                                        )

                                    WHEN MATCHED THEN
                                        UPDATE SET
                                            Definition = target.Definition   -- NO-OP UPDATE (forces OUTPUT)

                                    OUTPUT
                                        inserted.DictionaryEntryParsedId;
                                    """;

    private readonly string _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    private readonly ILogger<SqlParsedDefinitionWriter> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    ///     Writes a single parsed definition.
    /// </summary>
    public async Task<long> WriteAsync(
        long dictionaryEntryId,
        ParsedDefinition parsed,
        long? parentParsedId,
        CancellationToken ct)
    {
        if (parsed == null) throw new ArgumentNullException(nameof(parsed));

        try
        {
            var parameters = CreateParameters(dictionaryEntryId, parsed, parentParsedId);

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            var commandDefinition = new CommandDefinition(
                MergeSql,
                parameters,
                null,
                CommandTimeoutSeconds,
                CommandType.Text,
                cancellationToken: ct);

            var parsedId = await connection.ExecuteScalarAsync<long?>(commandDefinition);

            if (!parsedId.HasValue || parsedId <= 0)
            {
                _logger.LogDebug(
                    "ParsedDefinition MERGE returned no ID (likely already exists) | EntryId={EntryId}",
                    dictionaryEntryId);
                return -1;
            }

            _logger.LogDebug(
                "ParsedDefinition written | EntryId={EntryId} | ParsedId={ParsedId}",
                dictionaryEntryId, parsedId.Value);

            return parsedId.Value;
        }
        catch (SqlException sqlEx) when (sqlEx.Number == 2627 || sqlEx.Number == 2601)
        {
            _logger.LogDebug(
                "Duplicate parsed definition detected | EntryId={EntryId}",
                dictionaryEntryId);
            return -1;
        }
        catch (SqlException sqlEx) when (sqlEx.Number == 8152)
        {
            _logger.LogError(
                sqlEx,
                "Data truncation error | EntryId={EntryId} | Domain={Domain} | UsageLabel={UsageLabel}",
                dictionaryEntryId, parsed.Domain, parsed.UsageLabel);

            return await WriteWithTruncatedValuesAsync(dictionaryEntryId, parsed, parentParsedId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to write parsed definition | EntryId={EntryId} | ParentId={ParentId} | Title={Title}",
                dictionaryEntryId, parentParsedId, parsed.MeaningTitle);
            throw new DataException($"Failed to write parsed definition for DictionaryEntryId={dictionaryEntryId}", ex);
        }
    }

    /// <summary>
    ///     Attempts to write with aggressively truncated domain/usage values.
    /// </summary>
    private async Task<long> WriteWithTruncatedValuesAsync(
        long dictionaryEntryId,
        ParsedDefinition parsed,
        long? parentParsedId,
        CancellationToken ct)
    {
        try
        {
            var parameters = CreateParametersWithAggressiveTruncation(dictionaryEntryId, parsed, parentParsedId);

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            var commandDefinition = new CommandDefinition(
                MergeSql,
                parameters,
                null,
                CommandTimeoutSeconds,
                CommandType.Text,
                cancellationToken: ct);

            var parsedId = await connection.ExecuteScalarAsync<long?>(commandDefinition);

            if (!parsedId.HasValue || parsedId <= 0)
            {
                _logger.LogWarning(
                    "Failed to write parsed definition even with truncated values | EntryId={EntryId}",
                    dictionaryEntryId);
                return -1;
            }

            _logger.LogWarning(
                "ParsedDefinition written with truncated values | EntryId={EntryId} | ParsedId={ParsedId}",
                dictionaryEntryId, parsedId.Value);

            return parsedId.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to write parsed definition even with truncated values | EntryId={EntryId}",
                dictionaryEntryId);
            return -1;
        }
    }

    /// <summary>
    ///     Creates parameters for the SQL command, ensuring no NULL values for non-nullable columns.
    /// </summary>
    private object CreateParameters(
        long dictionaryEntryId,
        ParsedDefinition parsed,
        long? parentParsedId)
    {
        var meaningTitle = parsed.MeaningTitle;
        if (string.IsNullOrWhiteSpace(meaningTitle)) meaningTitle = "unnamed sense";

        return new
        {
            DictionaryEntryId = dictionaryEntryId,
            ParentParsedId = parentParsedId ?? (object)DBNull.Value,
            MeaningTitle = meaningTitle.Trim(),
            SenseNumber = parsed.SenseNumber ?? 0,
            DomainCode = ExtractDomainCode(parsed.Domain) ?? (object)DBNull.Value,
            UsageLabel = ExtractUsageLabel(parsed.UsageLabel) ?? (object)DBNull.Value,
            Definition = parsed.Definition?.Trim() ?? string.Empty,
            RawFragment = parsed.RawFragment?.Trim() ?? string.Empty
        };
    }

    /// <summary>
    ///     Creates parameters with aggressive truncation for domain/usage values.
    /// </summary>
    private object CreateParametersWithAggressiveTruncation(
        long dictionaryEntryId,
        ParsedDefinition parsed,
        long? parentParsedId)
    {
        var meaningTitle = parsed.MeaningTitle;
        if (string.IsNullOrWhiteSpace(meaningTitle)) meaningTitle = "unnamed sense";

        return new
        {
            DictionaryEntryId = dictionaryEntryId,
            ParentParsedId = parentParsedId ?? (object)DBNull.Value,
            MeaningTitle = meaningTitle.Trim(),
            SenseNumber = parsed.SenseNumber ?? 0,
            DomainCode = ExtractShortDomainCode(parsed.Domain) ?? (object)DBNull.Value,
            UsageLabel = ExtractShortUsageLabel(parsed.UsageLabel) ?? (object)DBNull.Value,
            Definition = parsed.Definition?.Trim() ?? string.Empty,
            RawFragment = parsed.RawFragment?.Trim() ?? string.Empty
        };
    }

    /// <summary>
    ///     Extracts a clean domain code from potentially long domain text.
    /// </summary>
    private static string? ExtractDomainCode(string? domainText)
    {
        if (string.IsNullOrWhiteSpace(domainText))
            return null;

        var trimmed = domainText.Trim();

        var shortCode = ExtractShortDomainCode(trimmed);
        if (!string.IsNullOrEmpty(shortCode))
            return shortCode;

        if (trimmed.Length <= DomainCodeMaxLength)
            return trimmed;

        return trimmed.Substring(0, DomainCodeMaxLength - 3) + "...";
    }

    /// <summary>
    ///     Extracts a short domain code from domain text.
    /// </summary>
    private static string? ExtractShortDomainCode(string? domainText)
    {
        if (string.IsNullOrWhiteSpace(domainText))
            return null;

        var domainCodes = new[]
        {
            "AM", "US", "BRIT", "UK", "FORMAL", "INFORMAL", "LITERARY",
            "OLD-FASHIONED", "TECHNICAL", "RARE", "OBSOLETE", "ARCHAIC",
            "COLLOQUIAL", "SLANG", "VULGAR", "OFFENSIVE", "HUMOROUS"
        };

        foreach (var code in domainCodes)
            if (domainText.IndexOf(code, StringComparison.OrdinalIgnoreCase) >= 0)
                return code;

        if (domainText.Contains("主美") || domainText.Contains("美式"))
            return "US";
        if (domainText.Contains("主英") || domainText.Contains("英式"))
            return "UK";
        if (domainText.Contains("正式"))
            return "FORMAL";
        if (domainText.Contains("非正式"))
            return "INFORMAL";

        return null;
    }

    /// <summary>
    ///     Extracts a clean usage label from potentially long usage text.
    /// </summary>
    private static string? ExtractUsageLabel(string? usageText)
    {
        if (string.IsNullOrWhiteSpace(usageText))
            return null;

        var trimmed = usageText.Trim();

        var shortLabel = ExtractShortUsageLabel(trimmed);
        if (!string.IsNullOrEmpty(shortLabel))
            return shortLabel;

        if (trimmed.Length <= UsageLabelMaxLength)
            return trimmed;

        return trimmed.Substring(0, UsageLabelMaxLength - 3) + "...";
    }

    /// <summary>
    ///     Extracts a short usage label from usage text.
    /// </summary>
    private static string? ExtractShortUsageLabel(string? usageText)
    {
        if (string.IsNullOrWhiteSpace(usageText))
            return null;

        var usagePatterns = new[]
        {
            "N-COUNT", "N-UNCOUNT", "VERB", "ADJ", "ADV", "PREP",
            "CONJ", "PRON", "DET", "EXCLAM", "PHRASAL VB", "PHR V",
            "V-LINK", "V-ERG", "V-RECIP", "V-T", "V-I"
        };

        foreach (var pattern in usagePatterns)
            if (usageText.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                return pattern;

        var match = Regex.Match(usageText, @"^([A-Za-z0-9\-\s]+)");
        if (match.Success)
        {
            var result = match.Value.Trim();
            if (result.Length <= UsageLabelMaxLength)
                return result;
        }

        return null;
    }

    /// <summary>
    ///     Batch writes multiple parsed definitions for better performance.
    /// </summary>
    public async Task<IReadOnlyList<long>> WriteBatchAsync(
        IEnumerable<(long DictionaryEntryId, ParsedDefinition Parsed, long? ParentParsedId)> items,
        CancellationToken ct)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));

        var itemList = items.ToList();
        if (!itemList.Any())
            return [];

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

            var results = new List<long>();

            foreach (var item in itemList)
            {
                var parameters = CreateParameters(item.DictionaryEntryId, item.Parsed, item.ParentParsedId);

                var commandDefinition = new CommandDefinition(
                    MergeSql,
                    parameters,
                    transaction,
                    CommandTimeoutSeconds,
                    CommandType.Text,
                    cancellationToken: ct);

                var parsedId = await connection.ExecuteScalarAsync<long?>(commandDefinition);

                if (parsedId.HasValue && parsedId > 0)
                    results.Add(parsedId.Value);
            }

            await transaction.CommitAsync(ct);

            _logger.LogDebug(
                "Batch write completed | Count={Count} | Successful={Successful}",
                itemList.Count, results.Count);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch write of parsed definitions failed");
            throw;
        }
    }
}