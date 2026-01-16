namespace DictionaryImporter.AITextKit.AI.Infrastructure.Implementations;

public class SqlAuditLogger : IAuditLogger
{
    private readonly string _connectionString;
    private readonly ILogger<SqlAuditLogger> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly AuditLoggerOptions _options;
    private readonly SemaphoreSlim _batchLock = new(1, 1);
    private readonly List<AuditLogEntry> _batchBuffer = new();
    private readonly Timer _batchTimer;

    public SqlAuditLogger(
        IOptions<DatabaseOptions> dbOptions,
        IOptions<AuditLoggerOptions> options,
        ILogger<SqlAuditLogger> logger)
    {
        _connectionString = dbOptions.Value.ConnectionString;
        _options = options.Value;
        _logger = logger;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        _batchTimer = new Timer(
            _ => ProcessBatchAsync().GetAwaiter().GetResult(),
            null,
            TimeSpan.FromSeconds(_options.BatchIntervalSeconds),
            TimeSpan.FromSeconds(_options.BatchIntervalSeconds));
    }

    public async Task LogRequestAsync(AuditLogEntry entry)
    {
        if (!_options.Enabled)
            return;

        try
        {
            if (_options.UseBatching)
            {
                await AddToBatchAsync(entry);
            }
            else
            {
                await InsertSingleEntryAsync(entry);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log audit entry for request {RequestId}", entry.RequestId);

            await LogToFileAsync(entry);
        }
    }

    private async Task AddToBatchAsync(AuditLogEntry entry)
    {
        await _batchLock.WaitAsync();
        try
        {
            _batchBuffer.Add(entry);

            if (_batchBuffer.Count >= _options.MaxBatchSize)
            {
                await ProcessBatchAsync();
            }
        }
        finally
        {
            _batchLock.Release();
        }
    }

    private async Task ProcessBatchAsync()
    {
        if (_batchBuffer.Count == 0)
            return;

        List<AuditLogEntry> batchToProcess;
        await _batchLock.WaitAsync();
        try
        {
            batchToProcess = new List<AuditLogEntry>(_batchBuffer);
            _batchBuffer.Clear();
        }
        finally
        {
            _batchLock.Release();
        }

        if (batchToProcess.Count == 0)
            return;

        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                var sql = @"
                    INSERT INTO RequestAuditLogs (
                        RequestId, ProviderName, Model, UserId, SessionId,
                        RequestType, PromptHash, PromptLength, ResponseLength,
                        TokensUsed, DurationMs, EstimatedCost, Currency,
                        Success, StatusCode, ErrorCode, ErrorMessage,
                        IpAddress, UserAgent, RequestMetadata, ResponseMetadata
                    )
                    VALUES (
                        @RequestId, @ProviderName, @Model, @UserId, @SessionId,
                        @RequestType, @PromptHash, @PromptLength, @ResponseLength,
                        @TokensUsed, @DurationMs, @EstimatedCost, @Currency,
                        @Success, @StatusCode, @ErrorCode, @ErrorMessage,
                        @IpAddress, @UserAgent, @RequestMetadata, @ResponseMetadata
                    );
                ";

                foreach (var entry in batchToProcess)
                {
                    var parameters = new
                    {
                        entry.RequestId,
                        entry.ProviderName,
                        entry.Model,
                        entry.UserId,
                        entry.SessionId,
                        RequestType = entry.RequestType.ToString(),
                        entry.PromptHash,
                        entry.PromptLength,
                        entry.ResponseLength,
                        entry.TokensUsed,
                        entry.DurationMs,
                        entry.EstimatedCost,
                        entry.Currency,
                        entry.Success,
                        entry.StatusCode,
                        entry.ErrorCode,
                        entry.ErrorMessage,
                        entry.IpAddress,
                        entry.UserAgent,
                        RequestMetadata = entry.RequestMetadata.Any() ?
                            JsonSerializer.Serialize(entry.RequestMetadata, _jsonOptions) : null,
                        ResponseMetadata = entry.ResponseMetadata.Any() ?
                            JsonSerializer.Serialize(entry.ResponseMetadata, _jsonOptions) : null
                    };

                    await connection.ExecuteAsync(sql, parameters, transaction);
                }

                await transaction.CommitAsync();

                _logger.LogDebug("Successfully logged {Count} audit entries in batch", batchToProcess.Count);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to process audit batch of {Count} entries", batchToProcess.Count);

                foreach (var entry in batchToProcess)
                {
                    try
                    {
                        await InsertSingleEntryAsync(entry);
                    }
                    catch
                    {
                        await LogToFileAsync(entry);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process audit batch");
        }
    }

    private async Task InsertSingleEntryAsync(AuditLogEntry entry)
    {
        await using var connection = new SqlConnection(_connectionString);

        var sql = @"
            INSERT INTO RequestAuditLogs (
                AuditId, RequestId, ProviderName, Model, UserId, SessionId,
                RequestType, PromptHash, PromptLength, ResponseLength,
                TokensUsed, DurationMs, EstimatedCost, Currency,
                Success, StatusCode, ErrorCode, ErrorMessage,
                IpAddress, UserAgent, RequestMetadata, ResponseMetadata
            )
            VALUES (
                @AuditId, @RequestId, @ProviderName, @Model, @UserId, @SessionId,
                @RequestType, @PromptHash, @PromptLength, @ResponseLength,
                @TokensUsed, @DurationMs, @EstimatedCost, @Currency,
                @Success, @StatusCode, @ErrorCode, @ErrorMessage,
                @IpAddress, @UserAgent, @RequestMetadata, @ResponseMetadata
            );
        ";

        var parameters = new
        {
            entry.AuditId,
            entry.RequestId,
            entry.ProviderName,
            entry.Model,
            entry.UserId,
            entry.SessionId,
            RequestType = entry.RequestType.ToString(),
            entry.PromptHash,
            entry.PromptLength,
            entry.ResponseLength,
            entry.TokensUsed,
            entry.DurationMs,
            entry.EstimatedCost,
            entry.Currency,
            entry.Success,
            entry.StatusCode,
            entry.ErrorCode,
            entry.ErrorMessage,
            entry.IpAddress,
            entry.UserAgent,
            RequestMetadata = entry.RequestMetadata.Any() ?
                JsonSerializer.Serialize(entry.RequestMetadata, _jsonOptions) : null,
            ResponseMetadata = entry.ResponseMetadata.Any() ?
                JsonSerializer.Serialize(entry.ResponseMetadata, _jsonOptions) : null
        };

        await connection.ExecuteAsync(sql, parameters);
    }

    public async Task<IEnumerable<AuditLogEntry>> GetRecentRequestsAsync(
        string providerName = null,
        string userId = null,
        int limit = 100)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);

            var sql = @"
                SELECT TOP (@Limit)
                    AuditId, RequestId, ProviderName, Model, UserId, SessionId,
                    RequestType, PromptHash, PromptLength, ResponseLength,
                    TokensUsed, DurationMs, EstimatedCost, Currency,
                    Success, StatusCode, ErrorCode, ErrorMessage,
                    IpAddress, UserAgent, RequestMetadata, ResponseMetadata,
                    CreatedAt
                FROM RequestAuditLogs
                WHERE (@ProviderName IS NULL OR ProviderName = @ProviderName)
                    AND (@UserId IS NULL OR UserId = @UserId)
                ORDER BY CreatedAt DESC;
            ";

            var results = await connection.QueryAsync<dynamic>(sql, new
            {
                ProviderName = providerName,
                UserId = userId,
                Limit = limit
            });

            return results.Select(r => new AuditLogEntry
            {
                AuditId = r.AuditId,
                RequestId = r.RequestId,
                ProviderName = r.ProviderName,
                Model = r.Model,
                UserId = r.UserId,
                SessionId = r.SessionId,
                RequestType = Enum.Parse<RequestType>(r.RequestType),
                PromptHash = r.PromptHash,
                PromptLength = r.PromptLength,
                ResponseLength = r.ResponseLength,
                TokensUsed = r.TokensUsed,
                DurationMs = r.DurationMs,
                EstimatedCost = r.EstimatedCost,
                Currency = r.Currency,
                Success = r.Success,
                StatusCode = r.StatusCode,
                ErrorCode = r.ErrorCode,
                ErrorMessage = r.ErrorMessage,
                IpAddress = r.IpAddress,
                UserAgent = r.UserAgent,
                RequestMetadata = !string.IsNullOrEmpty(r.RequestMetadata) ?
                    JsonSerializer.Deserialize<Dictionary<string, object>>(r.RequestMetadata, _jsonOptions) :
                    new Dictionary<string, object>(),
                ResponseMetadata = !string.IsNullOrEmpty(r.ResponseMetadata) ?
                    JsonSerializer.Deserialize<Dictionary<string, object>>(r.ResponseMetadata, _jsonOptions) :
                    new Dictionary<string, object>(),
                CreatedAt = r.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recent requests");
            return Enumerable.Empty<AuditLogEntry>();
        }
    }

    public async Task<IEnumerable<AuditSummary>> GetAuditSummaryAsync(DateTime from, DateTime to)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);

            var sql = @"
                SELECT
                    CAST(CreatedAt AS DATE) as Date,
                    ProviderName,
                    COUNT(*) as TotalRequests,
                    SUM(CASE WHEN Success = 1 THEN 1 ELSE 0 END) as SuccessfulRequests,
                    SUM(TokensUsed) as TotalTokens,
                    AVG(DurationMs) as AvgDurationMs,
                    SUM(EstimatedCost) as TotalCost
                FROM RequestAuditLogs
                WHERE CreatedAt >= @From AND CreatedAt <= @To
                GROUP BY CAST(CreatedAt AS DATE), ProviderName
                ORDER BY Date DESC, TotalRequests DESC;
            ";

            var results = await connection.QueryAsync<AuditSummaryRecord>(sql, new { From = from, To = to });

            return results.Select(r => new AuditSummary
            {
                Date = r.Date,
                ProviderName = r.ProviderName,
                TotalRequests = r.TotalRequests,
                SuccessfulRequests = r.SuccessfulRequests,
                SuccessRate = r.TotalRequests > 0 ? r.SuccessfulRequests * 100.0 / r.TotalRequests : 0,
                TotalTokens = r.TotalTokens,
                AvgDurationMs = r.AvgDurationMs,
                TotalCost = r.TotalCost ?? 0
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get audit summary");
            return Enumerable.Empty<AuditSummary>();
        }
    }

    private async Task LogToFileAsync(AuditLogEntry entry)
    {
        try
        {
            var logDir = Path.Combine(AppContext.BaseDirectory, "Logs", "Audit");
            Directory.CreateDirectory(logDir);

            var logFile = Path.Combine(logDir, $"audit_{DateTime.UtcNow:yyyyMMdd}.log");

            var logLine = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} | " +
                         $"RequestId: {entry.RequestId} | " +
                         $"Provider: {entry.ProviderName} | " +
                         $"User: {entry.UserId} | " +
                         $"Success: {entry.Success} | " +
                         $"Tokens: {entry.TokensUsed} | " +
                         $"Duration: {entry.DurationMs}ms";

            if (!entry.Success)
            {
                logLine += $" | Error: {entry.ErrorCode} - {entry.ErrorMessage}";
            }

            await File.AppendAllTextAsync(logFile, logLine + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log to file");
        }
    }

    public void Dispose()
    {
        _batchTimer?.Dispose();
        _batchLock?.Dispose();

        if (_batchBuffer.Count > 0)
        {
            ProcessBatchAsync().GetAwaiter().GetResult();
        }
    }
}