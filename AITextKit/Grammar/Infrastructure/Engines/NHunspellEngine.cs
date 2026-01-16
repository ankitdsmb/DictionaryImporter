using NHunspell;

namespace DictionaryImporter.AITextKit.Grammar.Infrastructure.Engines;

public sealed class NHunspellEngine(string dictionaryPath, ILogger<NHunspellEngine> logger)
    : IGrammarEngine, IDisposable
{
    private Hunspell? _hunspell;
    private bool _initialized = false;
    private readonly object _lock = new();

    public string Name => "NHunspell";
    public double ConfidenceWeight => 0.95;

    public bool IsSupported(string languageCode)
    {
        var supportedLanguages = new[] { "en", "en-US", "en-GB", "en-CA", "en-AU" };
        return supportedLanguages.Any(lang =>
            languageCode.StartsWith(lang, StringComparison.OrdinalIgnoreCase));
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        await Task.Run(() =>
        {
            lock (_lock)
            {
                try
                {
                    var dictionaryFiles = Directory.GetFiles(dictionaryPath, "*.dic", SearchOption.TopDirectoryOnly);

                    if (dictionaryFiles.Length == 0)
                    {
                        logger.LogWarning("No Hunspell dictionary files found at {Path}", dictionaryPath);
                        _hunspell = null;
                    }
                    else
                    {
                        var dicFile = dictionaryFiles[0];
                        var affFile = Path.ChangeExtension(dicFile, ".aff");

                        if (File.Exists(affFile))
                        {
                            _hunspell = new Hunspell(affFile, dicFile);
                            logger.LogInformation("Loaded Hunspell dictionary: {Dictionary}", Path.GetFileName(dicFile));
                        }
                        else
                        {
                            logger.LogWarning("Affix file not found for dictionary: {Dictionary}", dicFile);
                            _hunspell = null;
                        }
                    }

                    _initialized = true;
                    logger.LogInformation("NHunspell engine initialized: {Status}",
                        _hunspell != null ? "Success" : "No dictionary loaded");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to initialize NHunspell engine");
                    _hunspell = null;
                    _initialized = true;
                }
            }
        });
    }

    public async Task<GrammarCheckResult> CheckAsync(
        string text,
        string languageCode = "en-US",
        CancellationToken ct = default)
    {
        if (!_initialized)
        {
            await InitializeAsync();
        }

        if (_hunspell == null || string.IsNullOrWhiteSpace(text) || !IsSupported(languageCode))
        {
            return new GrammarCheckResult(false, 0, [], TimeSpan.Zero);
        }

        var sw = Stopwatch.StartNew();
        var issues = new List<GrammarIssue>();

        try
        {
            var words = Regex.Matches(text, @"\b[\w']+\b")
                .Cast<Match>()
                .Where(m => m.Length > 0)
                .Select(m => new
                {
                    Word = m.Value,
                    StartOffset = m.Index,
                    EndOffset = m.Index + m.Length
                })
                .ToList();

            foreach (var wordInfo in words)
            {
                ct.ThrowIfCancellationRequested();

                if (wordInfo.Word.Length < 2 || wordInfo.Word.Any(char.IsDigit))
                    continue;

                if (!_hunspell.Spell(wordInfo.Word))
                {
                    var suggestions = _hunspell.Suggest(wordInfo.Word);
                    var contextStart = Math.Max(0, wordInfo.StartOffset - 20);
                    var contextLength = Math.Min(40, text.Length - contextStart);
                    var context = text.Substring(contextStart, contextLength);

                    var issue = new GrammarIssue(
                        StartOffset: wordInfo.StartOffset,
                        EndOffset: wordInfo.EndOffset,
                        Message: $"Possible spelling error: '{wordInfo.Word}'",
                        ShortMessage: "Spelling",
                        Replacements: suggestions.Take(5).ToList(),
                        RuleId: $"SPELLING_{wordInfo.Word.ToUpperInvariant()}",
                        RuleDescription: $"Spelling check for '{wordInfo.Word}'",
                        Tags: new List<string> { "spelling" },
                        Context: context,
                        ContextOffset: Math.Max(0, wordInfo.StartOffset - 20),
                        ConfidenceLevel: 85
                    );

                    issues.Add(issue);
                }
            }

            sw.Stop();
            return new GrammarCheckResult(true, issues.Count, issues, sw.Elapsed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in NHunspell spell check");
            sw.Stop();
            return new GrammarCheckResult(false, 0, [], sw.Elapsed);
        }
    }

    public void Dispose()
    {
        _hunspell?.Dispose();
    }
}