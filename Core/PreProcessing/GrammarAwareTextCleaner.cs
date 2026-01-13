// File: DictionaryImporter.Core/PreProcessing/GrammarAwareTextCleaner.cs
using DictionaryImporter.Core.Grammar;
using DictionaryImporter.Infrastructure.Grammar;

namespace DictionaryImporter.Core.PreProcessing;

public static class GrammarAwareTextCleaner
{
    private static readonly IGrammarCorrector _grammarCorrector;

    static GrammarAwareTextCleaner()
    {
        // Lazy initialization with optional grammar correction
        _grammarCorrector = CreateGrammarCorrector();
    }

    public static async Task<string> CleanWithGrammarAsync(
        string text,
        bool applyAutoCorrection = true,
        string languageCode = "en-US",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Step 1: Apply existing cleaning logic
        var cleaned = await Task.Run(() => CleanTextBasic(text), ct);

        // Step 2: Apply grammar correction if enabled
        if (applyAutoCorrection && _grammarCorrector != null)
        {
            try
            {
                var correctionResult = await _grammarCorrector.AutoCorrectAsync(cleaned, languageCode, ct);
                if (correctionResult.AppliedCorrections.Any())
                {
                    cleaned = correctionResult.CorrectedText;
                }
            }
            catch
            {
                // Fallback to basic cleaning if grammar service fails
                // Logging would happen at a higher level
            }
        }

        return cleaned;
    }

    private static string CleanTextBasic(string text)
    {
        // Reuse existing cleaning logic from your codebase
        text = CjkPunctuationStripper.RemoveCjkPunctuation(text);
        text = CjkStripper.RemoveCjk(text);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text;
    }

    private static IGrammarCorrector CreateGrammarCorrector()
    {
        try
        {
            // Could be configured via appsettings
            var languageToolUrl = Environment.GetEnvironmentVariable("LANGUAGETOOL_URL") ?? "http://localhost:2026";
            return new LanguageToolGrammarCorrector(languageToolUrl);
        }
        catch
        {
            // Return a null object pattern implementation
            return new NoOpGrammarCorrector();
        }
    }

    private sealed class NoOpGrammarCorrector : IGrammarCorrector
    {
        public Task<GrammarCheckResult> CheckAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
            => Task.FromResult(new GrammarCheckResult(false, 0, Array.Empty<GrammarIssue>(), TimeSpan.Zero));

        public Task<GrammarCorrectionResult> AutoCorrectAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
            => Task.FromResult(new GrammarCorrectionResult(text, text, Array.Empty<AppliedCorrection>(), Array.Empty<GrammarIssue>()));

        public Task<IReadOnlyList<GrammarSuggestion>> SuggestImprovementsAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GrammarSuggestion>>(Array.Empty<GrammarSuggestion>());
    }
}