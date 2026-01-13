// File: DictionaryImporter/Core/Grammar/Simple/SimpleGrammarCorrector.cs
using DictionaryImporter.Core.Grammar;
using DictionaryImporter.Core.Grammar.Enhanced;
using DictionaryImporter.Infrastructure.Grammar;
using DictionaryImporter.Infrastructure.Grammar.Engines;
using Microsoft.Extensions.Configuration;

namespace DictionaryImporter.Core.Grammar.Simple;

public sealed class SimpleGrammarCorrector : IGrammarCorrector, IDisposable
{
    private readonly ILogger<SimpleGrammarCorrector> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly GrammarCorrectionSettings _settings;
    private readonly List<IGrammarEngine> _engines = new();
    private bool _disposed = false;

    // Main constructor
    public SimpleGrammarCorrector(
        string languageToolUrl,
        IConfiguration configuration,
        ILogger<SimpleGrammarCorrector> logger,
        ILoggerFactory loggerFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));

        _settings = new GrammarCorrectionSettings
        {
            MinDefinitionLength = configuration?.GetValue<int>("Grammar:MinDefinitionLength", 20) ?? 20,
            DefaultLanguage = configuration?.GetValue<string>("Grammar:DefaultLanguage", "en-US") ?? "en-US",
            LanguageToolUrl = languageToolUrl,
            HunspellDictionaryPath = configuration?["Grammar:HunspellDictionaryPath"] ?? "Dictionaries",
            PatternRulesPath = configuration?["Grammar:PatternRulesPath"] ?? "grammar-rules.json"
        };

        InitializeEngines();
    }

    // Simplified constructor for fallback/quick initialization
    public SimpleGrammarCorrector(string languageToolUrl, ILogger<SimpleGrammarCorrector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _settings = new GrammarCorrectionSettings
        {
            MinDefinitionLength = 20,
            DefaultLanguage = "en-US",
            LanguageToolUrl = languageToolUrl,
            HunspellDictionaryPath = "Dictionaries",
            PatternRulesPath = "grammar-rules.json"
        };

        // Create a minimal logger factory for internal use
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter("Microsoft", LogLevel.Warning)
                .AddFilter("System", LogLevel.Warning)
                .AddFilter("DictionaryImporter", LogLevel.Debug);
            //.AddSimpleConsole(options =>
            //{
            //    options.SingleLine = true;
            //    options.TimestampFormat = "HH:mm:ss ";
            //});
        });

        InitializeEngines();
    }

    // Minimal constructor for fallback
    private SimpleGrammarCorrector(ILogger<SimpleGrammarCorrector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _settings = new GrammarCorrectionSettings
        {
            MinDefinitionLength = 20,
            DefaultLanguage = "en-US",
            LanguageToolUrl = "http://localhost:2026",
            HunspellDictionaryPath = "Dictionaries",
            PatternRulesPath = "grammar-rules.json"
        };

        // Create a minimal logger factory for internal use
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddFilter("Microsoft", LogLevel.Warning)
                .AddFilter("System", LogLevel.Warning)
                .AddFilter("DictionaryImporter", LogLevel.Warning);
            //.AddSimpleConsole(options =>
            //{
            //    options.SingleLine = true;
            //    options.TimestampFormat = "HH:mm:ss ";
            //});
        });

        InitializeEngines();
    }

    private void InitializeEngines()
    {
        _logger.LogInformation("Initializing SimpleGrammarCorrector with LanguageTool at {Url}", _settings.LanguageToolUrl);

        // Initialize LanguageTool engine (primary engine)
        try
        {
            var languageToolLogger = _loggerFactory.CreateLogger<LanguageToolEngine>();
            var languageToolEngine = new LanguageToolEngine(_settings.LanguageToolUrl, languageToolLogger);
            _engines.Add(languageToolEngine);
            _logger.LogDebug("✓ LanguageTool engine initialized");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "✗ Failed to initialize LanguageTool engine");
        }

        // Initialize NHunspell engine if configured
        if (!string.IsNullOrEmpty(_settings.HunspellDictionaryPath))
        {
            try
            {
                var hunspellLogger = _loggerFactory.CreateLogger<NHunspellEngine>();
                var hunspellEngine = new NHunspellEngine(_settings.HunspellDictionaryPath, hunspellLogger);
                _engines.Add(hunspellEngine);
                _logger.LogDebug("✓ NHunspell engine initialized with path: {Path}", _settings.HunspellDictionaryPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "✗ Failed to initialize NHunspell engine");
            }
        }
        else
        {
            _logger.LogDebug("NHunspell engine disabled - no dictionary path configured");
        }

        // Initialize PatternRule engine
        try
        {
            var patternRuleLogger = _loggerFactory.CreateLogger<PatternRuleEngine>();
            var patternRuleEngine = new PatternRuleEngine(_settings.PatternRulesPath, patternRuleLogger);
            _engines.Add(patternRuleEngine);
            _logger.LogDebug("✓ PatternRule engine initialized");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "✗ Failed to initialize PatternRule engine");
        }

        // Initialize engines asynchronously
        _ = InitializeEnginesAsync();
    }

    private async Task InitializeEnginesAsync()
    {
        if (_engines.Count == 0)
        {
            _logger.LogWarning("No grammar engines available for initialization");
            return;
        }

        _logger.LogDebug("Asynchronously initializing {Count} grammar engines...", _engines.Count);

        var initTasks = _engines.Select(e => SafeInitializeAsync(e)).ToList();

        try
        {
            await Task.WhenAll(initTasks);
            var successfulInitializations = initTasks.Count(t => t.IsCompletedSuccessfully && t.Result);
            _logger.LogDebug("{Successful}/{Total} grammar engines initialized successfully",
                successfulInitializations, _engines.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Some grammar engines failed to initialize");
        }
    }

    private async Task<bool> SafeInitializeAsync(IGrammarEngine engine)
    {
        try
        {
            await engine.InitializeAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to initialize engine: {EngineName}", engine.Name);
            return false;
        }
    }

    public async Task<GrammarCheckResult> CheckAsync(
        string text,
        string languageCode = "en-US",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < _settings.MinDefinitionLength)
        {
            return new GrammarCheckResult(false, 0, Array.Empty<GrammarIssue>(), TimeSpan.Zero);
        }

        if (string.IsNullOrEmpty(languageCode))
            languageCode = _settings.DefaultLanguage;

        // Filter engines that support the language
        var supportedEngines = _engines.Where(e => e.IsSupported(languageCode)).ToList();
        if (supportedEngines.Count == 0)
        {
            return new GrammarCheckResult(false, 0, Array.Empty<GrammarIssue>(), TimeSpan.Zero);
        }

        var checkTasks = supportedEngines.Select(e => SafeCheckAsync(e, text, languageCode, ct)).ToList();

        try
        {
            await Task.WhenAll(checkTasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during grammar check");
        }

        var allIssues = new List<GrammarIssue>();
        var totalTime = TimeSpan.Zero;
        var completedChecks = 0;

        foreach (var task in checkTasks)
        {
            if (task.IsCompletedSuccessfully && task.Result != null)
            {
                var result = task.Result;
                allIssues.AddRange(result.Issues);
                totalTime += result.ElapsedTime;
                completedChecks++;
            }
        }

        // Deduplicate issues
        var uniqueIssues = DeduplicateIssues(allIssues);

        return new GrammarCheckResult(
            uniqueIssues.Count > 0,
            uniqueIssues.Count,
            uniqueIssues,
            totalTime
        );
    }

    private async Task<GrammarCheckResult?> SafeCheckAsync(
        IGrammarEngine engine,
        string text,
        string languageCode,
        CancellationToken ct)
    {
        try
        {
            return await engine.CheckAsync(text, languageCode, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Engine {EngineName} failed during check", engine.Name);
            return null;
        }
    }

    public async Task<GrammarCorrectionResult> AutoCorrectAsync(
        string text,
        string languageCode = "en-US",
        CancellationToken ct = default)
    {
        var checkResult = await CheckAsync(text, languageCode, ct);

        if (!checkResult.HasIssues)
        {
            return new GrammarCorrectionResult(
                text,
                text,
                Array.Empty<AppliedCorrection>(),
                Array.Empty<GrammarIssue>()
            );
        }

        // Apply corrections from end to start to maintain offsets
        var correctedText = text;
        var appliedCorrections = new List<AppliedCorrection>();

        // Group issues by position and rule to avoid conflicts
        var issueGroups = checkResult.Issues
            .Where(i => i.Replacements.Count > 0)
            .GroupBy(i => $"{i.StartOffset}-{i.EndOffset}-{i.RuleId}")
            .Select(g => g.OrderByDescending(i => i.ConfidenceLevel).First())
            .OrderByDescending(i => i.StartOffset)
            .ToList();

        foreach (var issue in issueGroups)
        {
            if (issue.Replacements.Count == 0)
                continue;

            // Use the highest confidence replacement
            var replacement = issue.Replacements[0];
            var originalSegment = correctedText.Substring(
                issue.StartOffset,
                issue.EndOffset - issue.StartOffset
            );

            // Skip if replacement would be identical (case-insensitive)
            if (string.Equals(originalSegment, replacement, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                correctedText = correctedText.Remove(
                    issue.StartOffset,
                    issue.EndOffset - issue.StartOffset
                ).Insert(issue.StartOffset, replacement);

                // Create AppliedCorrection - check if AppliedCorrection constructor needs fixing too
                appliedCorrections.Add(new AppliedCorrection(
                    originalSegment,
                    replacement,
                    issue.RuleId,
                    issue.Message,
                    issue.ConfidenceLevel
                ));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to apply correction for rule {RuleId}", issue.RuleId);
            }
        }

        var remainingIssues = checkResult.Issues
            .Where(i => !appliedCorrections.Any(a => a.RuleId == i.RuleId))
            .ToList();

        return new GrammarCorrectionResult(
            text,
            correctedText,
            appliedCorrections,
            remainingIssues
        );
    }

    public async Task<IReadOnlyList<GrammarSuggestion>> SuggestImprovementsAsync(
        string text,
        string languageCode = "en-US",
        CancellationToken ct = default)
    {
        var suggestions = new List<GrammarSuggestion>();

        if (string.IsNullOrWhiteSpace(text))
            return suggestions;

        // Basic readability suggestions
        if (text.Length > 100)
        {
            var sentenceCount = text.Count(c => c == '.' || c == '!' || c == '?');
            if (sentenceCount > 0)
            {
                var avgSentenceLength = text.Length / (double)sentenceCount;
                if (avgSentenceLength > 25)
                {
                    suggestions.Add(new GrammarSuggestion(
                        "Consider breaking long sentences",
                        "Long sentences can be hard to read. Try breaking them into shorter ones.",
                        "Readability",
                        "Readability improvement"
                    ));
                }
            }
        }

        return await Task.FromResult((IReadOnlyList<GrammarSuggestion>)suggestions);
    }

    private List<GrammarIssue> DeduplicateIssues(List<GrammarIssue> issues)
    {
        var uniqueIssues = new List<GrammarIssue>();
        var seen = new HashSet<string>();

        foreach (var issue in issues.OrderBy(i => i.StartOffset).ThenBy(i => i.EndOffset))
        {
            var key = $"{issue.StartOffset}-{issue.EndOffset}-{issue.RuleId}";

            // Check for overlap with existing issues
            var overlapping = uniqueIssues.FirstOrDefault(existing =>
                issue.StartOffset <= existing.EndOffset &&
                issue.EndOffset >= existing.StartOffset &&
                issue.RuleId == existing.RuleId);

            if (overlapping == null && seen.Add(key))
            {
                uniqueIssues.Add(issue);
            }
            else if (overlapping != null && issue.ConfidenceLevel > overlapping.ConfidenceLevel)
            {
                // Replace with higher confidence issue
                uniqueIssues.Remove(overlapping);
                uniqueIssues.Add(issue);
            }
        }

        return uniqueIssues;
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var engine in _engines.OfType<IDisposable>())
        {
            try
            {
                engine.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing grammar engine");
            }
        }

        if (_loggerFactory is IDisposable disposableLoggerFactory)
        {
            try
            {
                disposableLoggerFactory.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing logger factory");
            }
        }

        _engines.Clear();
        _disposed = true;
    }
}