using DictionaryImporter.AITextKit.Grammar.Core;
using DictionaryImporter.AITextKit.Grammar.Core.Models;
using DictionaryImporter.AITextKit.Grammar.Core.Results;

namespace DictionaryImporter.AITextKit.Grammar.Correctors;

public sealed class SettingsAwareGrammarCorrector : IGrammarCorrector
{
    private readonly EnhancedGrammarConfiguration _settings;
    private readonly IGrammarCorrector _inner;
    private readonly ILogger<SettingsAwareGrammarCorrector> _logger;

    public SettingsAwareGrammarCorrector(
        EnhancedGrammarConfiguration settings,
        IGrammarCorrector inner,
        ILogger<SettingsAwareGrammarCorrector> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<GrammarCheckResult> CheckAsync(
        string text,
        string languageCode = "en-US",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < _settings.MinDefinitionLength)
        {
            _logger.LogDebug(
                "Skipping grammar check because text length < MinDefinitionLength ({Len} < {Min})",
                text?.Length ?? 0,
                _settings.MinDefinitionLength);

            return Task.FromResult(new GrammarCheckResult(false, 0, [], TimeSpan.Zero));
        }

        if (string.IsNullOrWhiteSpace(languageCode))
            languageCode = _settings.DefaultLanguage;

        return _inner.CheckAsync(text, languageCode, ct);
    }

    public Task<GrammarCorrectionResult> AutoCorrectAsync(
        string text,
        string languageCode = "en-US",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < _settings.MinDefinitionLength)
        {
            return Task.FromResult(new GrammarCorrectionResult(
                text,
                text,
                [],
                []
            ));
        }

        if (string.IsNullOrWhiteSpace(languageCode))
            languageCode = _settings.DefaultLanguage;

        return _inner.AutoCorrectAsync(text, languageCode, ct);
    }

    public Task<IReadOnlyList<GrammarSuggestion>> SuggestImprovementsAsync(
        string text,
        string languageCode = "en-US",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult<IReadOnlyList<GrammarSuggestion>>([]);

        if (string.IsNullOrWhiteSpace(languageCode))
            languageCode = _settings.DefaultLanguage;

        return _inner.SuggestImprovementsAsync(text, languageCode, ct);
    }
}