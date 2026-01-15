using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DictionaryImporter.AI.Infrastructure.Implementations;

public class SqlQuotaManager : IQuotaManager, IDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<SqlQuotaManager> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly Timer _cleanupTimer;
    private readonly QuotaManagerOptions _options;

    public SqlQuotaManager(
        IOptions<DatabaseOptions> dbOptions,
        IOptions<QuotaManagerOptions> options,
        ILogger<SqlQuotaManager> logger)
    {
        _connectionString = dbOptions.Value.ConnectionString;
        _options = options.Value;
        _logger = logger;

        InitializeQuotasAsync().GetAwaiter().GetResult();

        _cleanupTimer = new Timer(
            _ => CleanupExpiredQuotasAsync().GetAwaiter().GetResult(),
            null,
            TimeSpan.FromMinutes(_options.CleanupIntervalMinutes),
            TimeSpan.FromMinutes(_options.CleanupIntervalMinutes));
    }

    private async Task InitializeQuotasAsync()
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);

            var providers = await GetActiveProvidersAsync();
            foreach (var provider in providers)
            {
                await EnsureQuotaEntriesExistAsync(provider);
            }

            _logger.LogInformation("Quota manager initialized for {ProviderCount} providers", providers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize quota manager");
            throw;
        }
    }

    public async Task<QuotaCheckResult> CheckQuotaAsync(
    string providerName,
    string userId = null,
    int estimatedTokens = 0,
    decimal estimatedCost = 0)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);

            var sql = @"
            SELECT
                pq.ProviderName,
                pq.UserId,
                pq.PeriodType,
                pq.PeriodStart,
                pq.RequestLimit,
                pq.TokenLimit,
                pq.CostLimit,
                pq.RequestsUsed,
                pq.TokensUsed,
                pq.CostUsed,
                pq.IsActive,
                CASE
                    WHEN pq.PeriodType = 'Minute' THEN DATEADD(MINUTE, 1, pq.PeriodStart)
                    WHEN pq.PeriodType = 'Hour' THEN DATEADD(HOUR, 1, pq.PeriodStart)
                    WHEN pq.PeriodType = 'Day' THEN DATEADD(DAY, 1, pq.PeriodStart)
                    WHEN pq.PeriodType = 'Month' THEN DATEADD(MONTH, 1, pq.PeriodStart)
                END as PeriodEnd
            FROM ProviderQuotas pq
            WHERE pq.ProviderName = @ProviderName
                AND pq.UserId = ISNULL(@UserId, '')
                AND pq.IsActive = 1
                AND pq.PeriodStart <= GETUTCDATE()
                AND (
                    pq.PeriodType = 'Minute' AND pq.PeriodStart >= DATEADD(MINUTE, -1, GETUTCDATE())
                    OR pq.PeriodType = 'Hour' AND pq.PeriodStart >= DATEADD(HOUR, -1, GETUTCDATE())
                    OR pq.PeriodType = 'Day' AND pq.PeriodStart >= DATEADD(DAY, -1, GETUTCDATE())
                    OR pq.PeriodType = 'Month' AND pq.PeriodStart >= DATEADD(MONTH, -1, GETUTCDATE())
                )
            ORDER BY
                CASE pq.PeriodType
                    WHEN 'Minute' THEN 1
                    WHEN 'Hour' THEN 2
                    WHEN 'Day' THEN 3
                    WHEN 'Month' THEN 4
                END;
        ";

            var quotas = await connection.QueryAsync<QuotaRecord>(sql, new
            {
                ProviderName = providerName,
                UserId = userId
            });

            var currentQuota = quotas.FirstOrDefault();
            if (currentQuota == null)
            {
                await EnsureQuotaEntriesExistAsync(providerName, userId);
                return await CheckQuotaAsync(providerName, userId, estimatedTokens, estimatedCost);
            }

            var canProceed = currentQuota.RequestsUsed + 1 <= currentQuota.RequestLimit &&
                            currentQuota.TokensUsed + estimatedTokens <= currentQuota.TokenLimit &&
                            (currentQuota.CostLimit == null ||
                             currentQuota.CostUsed + estimatedCost <= currentQuota.CostLimit.Value);

            var remainingRequests = Math.Max(0, currentQuota.RequestLimit - currentQuota.RequestsUsed);
            var remainingTokens = Math.Max(0, currentQuota.TokenLimit - currentQuota.TokensUsed);
            var remainingCost = currentQuota.CostLimit.HasValue ?
                Math.Max(0, currentQuota.CostLimit.Value - currentQuota.CostUsed) : decimal.MaxValue;

            return new QuotaCheckResult
            {
                CanProceed = canProceed,
                ProviderName = providerName,
                UserId = userId,
                RemainingRequests = remainingRequests,
                RemainingTokens = remainingTokens,
                RemainingCost = remainingCost,
                TimeUntilReset = currentQuota.PeriodEnd - DateTime.UtcNow,
                Limits = new QuotaLimits
                {
                    RequestLimit = currentQuota.RequestLimit,
                    TokenLimit = currentQuota.TokenLimit,
                    CostLimit = currentQuota.CostLimit
                },
                CurrentUsage = new QuotaUsage
                {
                    RequestsUsed = currentQuota.RequestsUsed,
                    TokensUsed = currentQuota.TokensUsed,
                    CostUsed = currentQuota.CostUsed
                },
                IsNearLimit = (remainingRequests * 100.0 / currentQuota.RequestLimit) < 20
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check quota for provider {Provider}", providerName);
            return new QuotaCheckResult
            {
                CanProceed = true,
                ProviderName = providerName,
                UserId = userId,
                RemainingRequests = int.MaxValue,
                RemainingTokens = long.MaxValue,
                RemainingCost = decimal.MaxValue,
                IsNearLimit = false
            };
        }
    }

    public async Task<QuotaUsageResult> RecordUsageAsync(
         string providerName,
         string userId = null,
         int tokensUsed = 0,
         decimal costUsed = 0,
         bool success = true)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var transaction = await connection.BeginTransactionAsync() as SqlTransaction;

            try
            {
                var periodStart = GetCurrentPeriodStart("Minute");

                var updateSql = @"
                    UPDATE ProviderQuotas
                    SET RequestsUsed = RequestsUsed + 1,
                        TokensUsed = TokensUsed + @TokensUsed,
                        CostUsed = CostUsed + @CostUsed,
                        UpdatedAt = GETUTCDATE()
                    WHERE ProviderName = @ProviderName
                        AND UserId = ISNULL(@UserId, '')
                        AND PeriodStart = @PeriodStart
                        AND PeriodType = 'Minute'
                        AND IsActive = 1;

                    IF @@ROWCOUNT = 0
                    BEGIN
                        INSERT INTO ProviderQuotas (
                            ProviderName, UserId, PeriodType, PeriodStart,
                            RequestLimit, TokenLimit, CostLimit,
                            RequestsUsed, TokensUsed, CostUsed
                        )
                        VALUES (
                            @ProviderName, ISNULL(@UserId, ''), 'Minute', @PeriodStart,
                            @DefaultRequestLimit, @DefaultTokenLimit, NULL,
                            1, @TokensUsed, @CostUsed
                        );
                    END
                ";

                var parameters = new
                {
                    ProviderName = providerName,
                    UserId = userId,
                    PeriodStart = periodStart,
                    TokensUsed = tokensUsed,
                    CostUsed = costUsed,
                    DefaultRequestLimit = _options.DefaultRequestLimit,
                    DefaultTokenLimit = _options.DefaultTokenLimit
                };

                await connection.ExecuteAsync(updateSql, parameters, transaction);

                await UpdateAggregateQuotasAsync(connection, transaction, providerName, userId, tokensUsed, costUsed);

                await transaction.CommitAsync();

                return new QuotaUsageResult
                {
                    ProviderName = providerName,
                    UserId = userId,
                    TokensUsed = tokensUsed,
                    CostUsed = costUsed,
                    Success = success,
                    RecordedAt = DateTime.UtcNow
                };
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record usage for provider {Provider}", providerName);
            throw;
        }
    }

    private async Task UpdateAggregateQuotasAsync(
        SqlConnection connection,
        SqlTransaction transaction, string providerName,
        string userId,
        int tokensUsed,
        decimal costUsed)
    {
        var periods = new[] { "Hour", "Day", "Month" };

        foreach (var period in periods)
        {
            var periodStart = GetCurrentPeriodStart(period);

            var sql = @"
                MERGE ProviderQuotas AS target
                USING (SELECT @ProviderName as ProviderName,
                              ISNULL(@UserId, '') as UserId,
                              @PeriodType as PeriodType,
                              @PeriodStart as PeriodStart) AS source
                ON target.ProviderName = source.ProviderName
                    AND target.UserId = source.UserId
                    AND target.PeriodType = source.PeriodType
                    AND target.PeriodStart = source.PeriodStart
                WHEN MATCHED THEN
                    UPDATE SET
                        RequestsUsed = RequestsUsed + 1,
                        TokensUsed = TokensUsed + @TokensUsed,
                        CostUsed = CostUsed + @CostUsed,
                        UpdatedAt = GETUTCDATE()
                WHEN NOT MATCHED THEN
                    INSERT (ProviderName, UserId, PeriodType, PeriodStart,
                            RequestLimit, TokenLimit, CostLimit,
                            RequestsUsed, TokensUsed, CostUsed, IsActive)
                    VALUES (@ProviderName, ISNULL(@UserId, ''), @PeriodType, @PeriodStart,
                            @DefaultRequestLimit, @DefaultTokenLimit, NULL,
                            1, @TokensUsed, @CostUsed, 1);
            ";

            var parameters = new
            {
                ProviderName = providerName,
                UserId = userId,
                PeriodType = period,
                PeriodStart = periodStart,
                TokensUsed = tokensUsed,
                CostUsed = costUsed,
                DefaultRequestLimit = GetDefaultLimitForPeriod(period),
                DefaultTokenLimit = GetDefaultTokenLimitForPeriod(period)
            };

            await connection.ExecuteAsync(sql, parameters, transaction);
        }
    }

    public async Task<IEnumerable<QuotaStatus>> GetProviderQuotasAsync(string providerName)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);

            var sql = @"
            SELECT
                ProviderName,
                UserId,
                PeriodType,
                PeriodStart,
                CASE
                    WHEN PeriodType = 'Minute' THEN DATEADD(MINUTE, 1, PeriodStart)
                    WHEN PeriodType = 'Hour' THEN DATEADD(HOUR, 1, PeriodStart)
                    WHEN PeriodType = 'Day' THEN DATEADD(DAY, 1, PeriodStart)
                    WHEN PeriodType = 'Month' THEN DATEADD(MONTH, 1, PeriodStart)
                END as PeriodEnd,
                RequestLimit,
                TokenLimit,
                CostLimit,
                RequestsUsed,
                TokensUsed,
                CostUsed,
                IsActive
            FROM ProviderQuotas
            WHERE ProviderName = @ProviderName
                AND IsActive = 1
            ORDER BY PeriodStart DESC;
        ";

            var records = await connection.QueryAsync<QuotaRecord>(sql, new { ProviderName = providerName });

            return records.Select(r => new QuotaStatus
            {
                ProviderName = r.ProviderName,
                UserId = r.UserId,
                PeriodType = r.PeriodType,
                PeriodStart = r.PeriodStart,
                PeriodEnd = r.PeriodEnd,
                RequestLimit = r.RequestLimit,
                RequestsUsed = r.RequestsUsed,
                TokenLimit = r.TokenLimit,
                TokensUsed = r.TokensUsed,
                CostLimit = r.CostLimit,
                CostUsed = r.CostUsed
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get provider quotas for {Provider}", providerName);
            return Enumerable.Empty<QuotaStatus>();
        }
    }

    public async Task<IEnumerable<QuotaStatus>> GetUserQuotasAsync(string userId)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);

            var sql = @"
            SELECT
                ProviderName,
                UserId,
                PeriodType,
                PeriodStart,
                CASE
                    WHEN PeriodType = 'Minute' THEN DATEADD(MINUTE, 1, PeriodStart)
                    WHEN PeriodType = 'Hour' THEN DATEADD(HOUR, 1, PeriodStart)
                    WHEN PeriodType = 'Day' THEN DATEADD(DAY, 1, PeriodStart)
                    WHEN PeriodType = 'Month' THEN DATEADD(MONTH, 1, PeriodStart)
                END as PeriodEnd,
                RequestLimit,
                TokenLimit,
                CostLimit,
                RequestsUsed,
                TokensUsed,
                CostUsed,
                IsActive
            FROM ProviderQuotas
            WHERE UserId = @UserId
                AND IsActive = 1
            ORDER BY ProviderName, PeriodStart DESC;
        ";

            var records = await connection.QueryAsync<QuotaRecord>(sql, new { UserId = userId });

            return records.Select(r => new QuotaStatus
            {
                ProviderName = r.ProviderName,
                UserId = r.UserId,
                PeriodType = r.PeriodType,
                PeriodStart = r.PeriodStart,
                PeriodEnd = r.PeriodEnd,
                RequestLimit = r.RequestLimit,
                RequestsUsed = r.RequestsUsed,
                TokenLimit = r.TokenLimit,
                TokensUsed = r.TokensUsed,
                CostLimit = r.CostLimit,
                CostUsed = r.CostUsed
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user quotas for {UserId}", userId);
            return Enumerable.Empty<QuotaStatus>();
        }
    }

    private async Task EnsureQuotaEntriesExistAsync(string providerName, string userId = null)
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);

            var periods = new[] { "Minute", "Hour", "Day", "Month" };

            foreach (var period in periods)
            {
                var periodStart = GetCurrentPeriodStart(period);

                var checkSql = @"
                    IF NOT EXISTS (
                        SELECT 1 FROM ProviderQuotas
                        WHERE ProviderName = @ProviderName
                            AND UserId = ISNULL(@UserId, '')
                            AND PeriodType = @PeriodType
                            AND PeriodStart = @PeriodStart
                    )
                    BEGIN
                        INSERT INTO ProviderQuotas (
                            ProviderName, UserId, PeriodType, PeriodStart,
                            RequestLimit, TokenLimit, CostLimit,
                            RequestsUsed, TokensUsed, CostUsed, IsActive
                        )
                        VALUES (
                            @ProviderName, ISNULL(@UserId, ''), @PeriodType, @PeriodStart,
                            @RequestLimit, @TokenLimit, NULL,
                            0, 0, 0, 1
                        );
                    END
                ";

                var parameters = new
                {
                    ProviderName = providerName,
                    UserId = userId,
                    PeriodType = period,
                    PeriodStart = periodStart,
                    RequestLimit = GetDefaultLimitForPeriod(period),
                    TokenLimit = GetDefaultTokenLimitForPeriod(period)
                };

                await connection.ExecuteAsync(checkSql, parameters);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure quota entries for provider {Provider}", providerName);
        }
    }

    private DateTime GetCurrentPeriodStart(string periodType)
    {
        var now = DateTime.UtcNow;

        return periodType switch
        {
            "Minute" => new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0),
            "Hour" => new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0),
            "Day" => now.Date,
            "Month" => new DateTime(now.Year, now.Month, 1),
            _ => throw new ArgumentException($"Invalid period type: {periodType}")
        };
    }

    private int GetDefaultLimitForPeriod(string periodType)
    {
        return periodType switch
        {
            "Minute" => _options.DefaultRequestsPerMinute,
            "Hour" => _options.DefaultRequestsPerHour,
            "Day" => _options.DefaultRequestsPerDay,
            "Month" => _options.DefaultRequestsPerMonth,
            _ => _options.DefaultRequestLimit
        };
    }

    private long GetDefaultTokenLimitForPeriod(string periodType)
    {
        return periodType switch
        {
            "Minute" => _options.DefaultTokensPerMinute,
            "Hour" => _options.DefaultTokensPerHour,
            "Day" => _options.DefaultTokensPerDay,
            "Month" => _options.DefaultTokensPerMonth,
            _ => _options.DefaultTokenLimit
        };
    }

    public async Task ResetExpiredQuotasAsync()
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);

            var sql = @"
                -- Deactivate expired quotas
                UPDATE ProviderQuotas
                SET IsActive = 0,
                    UpdatedAt = GETUTCDATE()
                WHERE IsActive = 1
                    AND PeriodEnd < GETUTCDATE();

                -- Create new quotas for current period
                INSERT INTO ProviderQuotas (ProviderName, UserId, PeriodType, PeriodStart,
                                            RequestLimit, TokenLimit, CostLimit,
                                            RequestsUsed, TokensUsed, CostUsed, IsActive)
                SELECT DISTINCT
                    ProviderName,
                    UserId,
                    PeriodType,
                    CASE PeriodType
                        WHEN 'Minute' THEN DATEADD(MINUTE, DATEDIFF(MINUTE, 0, GETUTCDATE()), 0)
                        WHEN 'Hour' THEN DATEADD(HOUR, DATEDIFF(HOUR, 0, GETUTCDATE()), 0)
                        WHEN 'Day' THEN CAST(GETUTCDATE() AS DATE)
                        WHEN 'Month' THEN DATEFROMPARTS(YEAR(GETUTCDATE()), MONTH(GETUTCDATE()), 1)
                    END as PeriodStart,
                    RequestLimit,
                    TokenLimit,
                    CostLimit,
                    0, 0, 0, 1
                FROM ProviderQuotas
                WHERE IsActive = 0
                    AND NOT EXISTS (
                        SELECT 1 FROM ProviderQuotas pq2
                        WHERE pq2.ProviderName = ProviderQuotas.ProviderName
                            AND pq2.UserId = ProviderQuotas.UserId
                            AND pq2.PeriodType = ProviderQuotas.PeriodType
                            AND pq2.IsActive = 1
                    );
            ";

            var affected = await connection.ExecuteAsync(sql);
            _logger.LogInformation("Reset {Count} expired quotas", affected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset expired quotas");
        }
    }

    private async Task CleanupExpiredQuotasAsync()
    {
        try
        {
            using var connection = new SqlConnection(_connectionString);

            var sql = @"
                -- Delete old quota records (keep last 90 days)
                DELETE FROM ProviderQuotas
                WHERE CreatedAt < DATEADD(DAY, -90, GETUTCDATE())
                    AND IsActive = 0;

                -- Delete old audit logs (keep last 30 days)
                DELETE FROM RequestAuditLogs
                WHERE CreatedAt < DATEADD(DAY, -30, GETUTCDATE());
            ";

            var deleted = await connection.ExecuteAsync(sql);
            if (deleted > 0)
            {
                _logger.LogDebug("Cleaned up {Count} old records", deleted);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup expired records");
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        _semaphore?.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<List<string>> GetActiveProvidersAsync()
    {
        return new List<string>
        {
            "OpenRouter", "Anthropic", "Gemini", "TogetherAI", "Cohere",
            "AI21", "TextCortex", "Perplexity", "NLPCloud", "HuggingFace",
            "DeepAI", "Watson", "AlephAlpha", "Replicate", "Ollama",
            "StabilityAI", "ElevenLabs", "AssemblyAI"
        };
    }
}

internal class QuotaRecord
{
    public string ProviderName { get; set; }
    public string UserId { get; set; }
    public string PeriodType { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int RequestLimit { get; set; }
    public long TokenLimit { get; set; }
    public decimal? CostLimit { get; set; }
    public int RequestsUsed { get; set; }
    public long TokensUsed { get; set; }
    public decimal CostUsed { get; set; }
    public bool IsActive { get; set; }
}

public class QuotaManagerOptions
{
    public int DefaultRequestLimit { get; set; } = 100;
    public long DefaultTokenLimit { get; set; } = 1000000;
    public int DefaultRequestsPerMinute { get; set; } = 60;
    public int DefaultRequestsPerHour { get; set; } = 1000;
    public int DefaultRequestsPerDay { get; set; } = 10000;
    public int DefaultRequestsPerMonth { get; set; } = 100000;
    public long DefaultTokensPerMinute { get; set; } = 90000;
    public long DefaultTokensPerHour { get; set; } = 500000;
    public long DefaultTokensPerDay { get; set; } = 2000000;
    public long DefaultTokensPerMonth { get; set; } = 10000000;
    public int CleanupIntervalMinutes { get; set; } = 5;
}