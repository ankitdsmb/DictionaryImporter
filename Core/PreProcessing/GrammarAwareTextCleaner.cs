namespace DictionaryImporter.Core.PreProcessing;

public static class GrammarAwareTextCleaner
{
    private static readonly IGrammarCorrector GrammarCorrector;

    static GrammarAwareTextCleaner()
    {
        GrammarCorrector = CreateGrammarCorrector();
    }

    public static async Task<string> CleanWithGrammarAsync(
        string text,
        bool applyAutoCorrection = true,
        string languageCode = "en-US",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var cleaned = await Task.Run(() => CleanTextBasic(text), ct);

        if (applyAutoCorrection && GrammarCorrector != null)
        {
            try
            {
                var correctionResult = await GrammarCorrector.AutoCorrectAsync(cleaned, languageCode, ct);
                if (correctionResult.AppliedCorrections.Any())
                {
                    cleaned = correctionResult.CorrectedText;
                }
            }
            catch
            {
            }
        }

        return cleaned;
    }

    private static string CleanTextBasic(string text)
    {
        text = CjkPunctuationStripper.RemoveCjkPunctuation(text);
        text = CjkStripper.RemoveCjk(text);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text;
    }

    private static IGrammarCorrector CreateGrammarCorrector()
    {
        try
        {
            var languageToolUrl = Environment.GetEnvironmentVariable("LANGUAGETOOL_URL") ?? "http://localhost:2026";
            return new LanguageToolGrammarCorrector(languageToolUrl);
        }
        catch
        {
            return new NoOpGrammarCorrector();
        }
    }

    private sealed class NoOpGrammarCorrector : IGrammarCorrector
    {
        public Task<GrammarCheckResult> CheckAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
            => Task.FromResult(new GrammarCheckResult(false, 0, [], TimeSpan.Zero));

        public Task<GrammarCorrectionResult> AutoCorrectAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
            => Task.FromResult(new GrammarCorrectionResult(text, text, [], []));

        public Task<IReadOnlyList<GrammarSuggestion>> SuggestImprovementsAsync(string text, string languageCode = "en-US", CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GrammarSuggestion>>([]);
    }
}