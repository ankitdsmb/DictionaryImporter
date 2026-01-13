// File: DictionaryImporter.Infrastructure/Grammar/GrammarAwareTransformer.cs
using DictionaryImporter.Core.Grammar;

namespace DictionaryImporter.Infrastructure.Grammar;

public sealed class GrammarAwareTransformer<T>(
    IDataTransformer<T> innerTransformer,
    IGrammarCorrector grammarCorrector,
    ILogger<GrammarAwareTransformer<T>> logger,
    GrammarCorrectionSetting? settings = null) : IDataTransformer<T>
{
    private readonly GrammarCorrectionSetting _settings = settings ?? new GrammarCorrectionSetting();

    public IEnumerable<DictionaryEntry> Transform(T raw)
    {
        var entries = innerTransformer.Transform(raw);

        foreach (var entry in entries)
        {
            var correctedEntry = ApplyGrammarCorrection(entry).GetAwaiter().GetResult();
            yield return correctedEntry;
        }
    }

    private async Task<DictionaryEntry> ApplyGrammarCorrection(DictionaryEntry entry)
    {
        // Skip if definition is too short
        if (string.IsNullOrWhiteSpace(entry.Definition) ||
            entry.Definition.Length < _settings.MinDefinitionLength)
        {
            return entry;
        }

        try
        {
            var correctedDefinition = await ApplyCorrectionToText(
                entry.Definition,
                "definition",
                entry.SourceCode);

            // Return entry with corrected definition
            return new DictionaryEntry
            {
                Word = entry.Word,
                NormalizedWord = entry.NormalizedWord,
                PartOfSpeech = entry.PartOfSpeech,
                Definition = correctedDefinition,
                Etymology = entry.Etymology,
                SenseNumber = entry.SenseNumber,
                SourceCode = entry.SourceCode,
                CreatedUtc = entry.CreatedUtc
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Grammar correction failed for word '{Word}' from source {Source}",
                entry.Word, entry.SourceCode);
            return entry; // Return original on failure
        }
    }

    private async Task<string> ApplyCorrectionToText(string text, string context, string sourceCode)
    {
        if (!_settings.EnabledForSource(sourceCode))
            return text;

        var correctionResult = await grammarCorrector.AutoCorrectAsync(
            text,
            _settings.GetLanguageCode(sourceCode));

        if (correctionResult.AppliedCorrections.Any())
        {
            logger.LogDebug(
                "Applied {Count} grammar corrections to {Context} for source {Source}",
                correctionResult.AppliedCorrections.Count,
                context,
                sourceCode);
        }

        return correctionResult.CorrectedText;
    }
}

public sealed class GrammarCorrectionSetting
{
    public bool Enabled { get; set; } = true;
    public int MinDefinitionLength { get; set; } = 20;
    public Dictionary<string, bool> SourceSettings { get; set; } = new();
    public Dictionary<string, string> LanguageMappings { get; set; } = new();
    public string LanguageToolUrl { get; internal set; }

    public bool EnabledForSource(string sourceCode)
    {
        return Enabled &&
               (!SourceSettings.TryGetValue(sourceCode, out var enabled) || enabled);
    }

    public string GetLanguageCode(string sourceCode)
    {
        return LanguageMappings.TryGetValue(sourceCode, out var lang)
            ? lang
            : "en-US";
    }
}