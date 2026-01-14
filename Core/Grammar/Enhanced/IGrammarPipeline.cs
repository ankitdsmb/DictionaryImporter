namespace DictionaryImporter.Core.Grammar.Enhanced;

public interface IGrammarPipeline : IGrammarCorrector
{
    /// <summary>
    /// Diagnostic information about which engines contributed
    /// </summary>
    Task<GrammarPipelineDiagnostics> AnalyzeAsync(string text, string languageCode = "en-US", CancellationToken ct = default);

    /// <summary>
    /// Confidence-weighted blended corrections
    /// </summary>
    Task<GrammarBlendedResult> GetBlendedCorrectionsAsync(string text, string languageCode = "en-US", CancellationToken ct = default);

    /// <summary>
    /// Train custom rules from user feedback
    /// </summary>
    Task<bool> TrainFromFeedbackAsync(GrammarFeedback feedback, CancellationToken ct = default);
}