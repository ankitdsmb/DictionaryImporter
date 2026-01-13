// File: DictionaryImporter/Infrastructure/Grammar/Engines/NHunspellEngine.cs
using DictionaryImporter.Core.Grammar;
using DictionaryImporter.Core.Grammar.Enhanced;
using NHunspell;
using System.Diagnostics;
using DictionaryImporter.Infrastructure.Grammar.Helper;

namespace DictionaryImporter.Infrastructure.Grammar.Engines;

public sealed class NHunspellEngine : IGrammarEngine, IDisposable
{
    private readonly string _dictionaryPath;
    private readonly ILogger<NHunspellEngine> _logger;
    private Hunspell? _hunspell;
    private readonly object _lock = new();
    private bool _initialized = false;

    public string Name => "NHunspell";
    public double ConfidenceWeight => 0.95;

    public NHunspellEngine(string dictionaryPath, ILogger<NHunspellEngine> logger)
    {
        _dictionaryPath = dictionaryPath ?? throw new ArgumentNullException(nameof(dictionaryPath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        await Task.Run(() =>
        {
            try
            {
                lock (_lock)
                {
                    if (!Directory.Exists(_dictionaryPath))
                    {
                        _logger.LogWarning("Dictionary path does not exist: {Path}", _dictionaryPath);
                        return;
                    }

                    // Look for dictionary files
                    var dicFiles = Directory.GetFiles(_dictionaryPath, "*.dic");
                    var affFiles = Directory.GetFiles(_dictionaryPath, "*.aff");

                    if (dicFiles.Length == 0 || affFiles.Length == 0)
                    {
                        _logger.LogWarning("No dictionary files found in {Path}", _dictionaryPath);
                        return;
                    }

                    // Use the first found dictionary/affix pair with matching names
                    foreach (var dicFile in dicFiles)
                    {
                        var baseName = Path.GetFileNameWithoutExtension(dicFile);
                        var affFile = Path.Combine(_dictionaryPath, baseName + ".aff");

                        if (File.Exists(affFile))
                        {
                            _hunspell = new Hunspell(affFile, dicFile);
                            _initialized = true;
                            _logger.LogInformation(
                                "NHunspell engine initialized with dictionary: {Dictionary}",
                                Path.GetFileName(dicFile)
                            );
                            break;
                        }
                    }

                    if (!_initialized)
                    {
                        _logger.LogWarning("Could not find matching .aff and .dic files in {Path}", _dictionaryPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize NHunspell engine");
            }
        });
    }

    //public async Task<GrammarCheckResult> CheckAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
    //{
    //    if (!_initialized || _hunspell == null || string.IsNullOrWhiteSpace(text))
    //        return new GrammarCheckResult(false, 0, Array.Empty<GrammarIssue>(), TimeSpan.Zero);

    //    var sw = Stopwatch.StartNew();
    //    var issues = new List<GrammarIssue>();

    //    await Task.Run(() =>
    //    {
    //        try
    //        {
    //            // Simple word boundary detection
    //            var wordPattern = new Regex(@"\b[a-zA-Z]+(?:'[a-zA-Z]+)?\b", RegexOptions.Compiled);
    //            var matches = wordPattern.Matches(text);

    //            foreach (Match match in matches)
    //            {
    //                ct.ThrowIfCancellationRequested();

    //                var word = match.Value;
    //                if (string.IsNullOrEmpty(word) || word.Length < 2) continue;

    //                // Skip proper nouns (starting with capital in middle of sentence)
    //                if (char.IsUpper(word[0]) && match.Index > 0 &&
    //                    char.IsLetterOrDigit(text[match.Index - 1]))
    //                    continue;

    //                // Check spelling
    //                if (!_hunspell.Spell(word))
    //                {
    //                    var suggestions = _hunspell.Suggest(word);

    //                    // Filter suggestions
    //                    var validSuggestions = suggestions
    //                        .Where(s => !string.IsNullOrEmpty(s) && s.Length >= 2)
    //                        .Take(3)
    //                        .ToList();

    //                    if (validSuggestions.Any())
    //                    {
    //                        issues.Add(new GrammarIssue(
    //                            StartOffset: match.Index,
    //                            EndOffset: match.Index + match.Length,
    //                            Message: $"Possible spelling error: '{word}'",
    //                            ShortMessage: "Spelling",
    //                            Replacements: validSuggestions,
    //                            RuleId: $"SPELLING_{word.ToUpperInvariant()}",
    //                            RuleDescription: $"Spelling check for '{word}'",
    //                            Tags: new List<string> { "spelling" },
    //                            Context: GetContext(text, match.Index),
    //                            ContextOffset: Math.Max(0, match.Index - 20),
    //                            ConfidenceLevel: 85
    //                        ));
    //                    }
    //                }
    //            }

    //            // Check for repeated words
    //            for (int i = 0; i < matches.Count - 1; i++)
    //            {
    //                var current = matches[i];
    //                var next = matches[i + 1];

    //                // Check if words are adjacent and identical (case-insensitive)
    //                if (next.Index == current.Index + current.Length + 1 && // +1 for space
    //                    string.Equals(current.Value, next.Value, StringComparison.OrdinalIgnoreCase))
    //                {
    //                    issues.Add(new GrammarIssue(
    //                        StartOffset: current.Index,
    //                        EndOffset: next.Index + next.Length,
    //                        Message: $"Repeated word: '{current.Value}'",
    //                        ShortMessage: "Repeated word",
    //                        Replacements: new List<string> { current.Value },
    //                        RuleId: "REPEATED_WORD",
    //                        RuleDescription: "Repeated word detection",
    //                        Tags: new List<string> { "style", "repetition" },
    //                        Context: GetContext(text, current.Index),
    //                        ContextOffset: Math.Max(0, current.Index - 20),
    //                        ConfidenceLevel: 90
    //                    ));
    //                }
    //            }
    //        }
    //        catch (Exception ex)
    //        {
    //            _logger.LogError(ex, "Error during NHunspell spell check");
    //        }
    //    }, ct);

    //    sw.Stop();
    //    return new GrammarCheckResult(issues.Count > 0, issues.Count, issues, sw.Elapsed);
    //}
    // File: DictionaryImporter/Infrastructure/Grammar/Engines/NHunspellEngine.cs (updated section)
    public async Task<GrammarCheckResult> CheckAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
    {
        if (!_initialized || _hunspell == null || string.IsNullOrWhiteSpace(text))
            return new GrammarCheckResult(false, 0, Array.Empty<GrammarIssue>(), TimeSpan.Zero);

        var sw = Stopwatch.StartNew();
        var issues = new List<GrammarIssue>();

        await Task.Run(() =>
        {
            try
            {
                // Simple word boundary detection
                var wordPattern = new Regex(@"\b[a-zA-Z]+(?:'[a-zA-Z]+)?\b", RegexOptions.Compiled);
                var matches = wordPattern.Matches(text);

                foreach (Match match in matches)
                {
                    ct.ThrowIfCancellationRequested();

                    var word = match.Value;
                    if (string.IsNullOrEmpty(word) || word.Length < 2) continue;

                    // Skip proper nouns (starting with capital in middle of sentence)
                    if (char.IsUpper(word[0]) && match.Index > 0 &&
                        char.IsLetterOrDigit(text[match.Index - 1]))
                        continue;

                    // Check spelling
                    if (!_hunspell.Spell(word))
                    {
                        var suggestions = _hunspell.Suggest(word);

                        // Filter suggestions
                        var validSuggestions = suggestions
                            .Where(s => !string.IsNullOrEmpty(s) && s.Length >= 2)
                            .Take(3)
                            .ToList();

                        if (validSuggestions.Any())
                        {
                            var issue = GrammarIssueHelper.CreateSpellingIssue(
                                match.Index,
                                match.Index + match.Length,
                                word,
                                validSuggestions,
                                GetContext(text, match.Index),
                                85
                            );
                            issues.Add(issue);
                        }
                    }
                }

                // Check for repeated words
                for (int i = 0; i < matches.Count - 1; i++)
                {
                    var current = matches[i];
                    var next = matches[i + 1];

                    // Check if words are adjacent and identical (case-insensitive)
                    if (next.Index == current.Index + current.Length + 1 && // +1 for space
                        string.Equals(current.Value, next.Value, StringComparison.OrdinalIgnoreCase))
                    {
                        var repeatedIssue = GrammarIssueHelper.CreateRepeatedWordIssue(
                            current.Index,
                            next.Index + next.Length,
                            current.Value,
                            90
                        );
                        issues.Add(repeatedIssue with
                        {
                            Context = GetContext(text, current.Index),
                            ContextOffset = Math.Max(0, current.Index - 20)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during NHunspell spell check");
            }
        }, ct);

        sw.Stop();
        return new GrammarCheckResult(issues.Count > 0, issues.Count, issues, sw.Elapsed);
    }

    private string GetContext(string text, int position, int contextLength = 50)
    {
        var start = Math.Max(0, position - contextLength);
        var end = Math.Min(text.Length, position + contextLength);
        return text.Substring(start, end - start);
    }

    public bool IsSupported(string languageCode)
    {
        // NHunspell primarily supports English variants
        return languageCode.StartsWith("en", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _hunspell?.Dispose();
            _hunspell = null;
            _initialized = false;
        }
    }
}