// File: DictionaryImporter.Infrastructure/Grammar/GrammarAwareTransformerFactory.cs
using DictionaryImporter.Core.Grammar;

namespace DictionaryImporter.Infrastructure.Grammar;

public sealed class GrammarAwareTransformerFactory(
    IGrammarCorrector grammarCorrector,
    GrammarCorrectionSettings settings,
    ILoggerFactory loggerFactory)
{
    public IDataTransformer<T> CreateGrammarAwareTransformer<T>(IDataTransformer<T> innerTransformer)
    {
        return new GrammarAwareTransformerWrapper<T>(
            innerTransformer,
            grammarCorrector,
            settings,
            loggerFactory.CreateLogger<GrammarAwareTransformerWrapper<T>>());
    }
}

internal sealed class GrammarAwareTransformerWrapper<T>(
    IDataTransformer<T> innerTransformer,
    IGrammarCorrector grammarCorrector,
    GrammarCorrectionSettings settings,
    ILogger<GrammarAwareTransformerWrapper<T>> logger) : IDataTransformer<T>
{
    public IEnumerable<DictionaryEntry> Transform(T raw)
    {
        var entries = innerTransformer.Transform(raw);

        foreach (var entry in entries)
        {
            yield return ApplyGrammarCorrectionSync(entry);
        }
    }

    private DictionaryEntry ApplyGrammarCorrectionSync(DictionaryEntry entry)
    {
        if (!settings.EnabledForSource(entry.SourceCode) ||
            string.IsNullOrWhiteSpace(entry.Definition) ||
            entry.Definition.Length < settings.MinDefinitionLength)
        {
            return entry;
        }

        try
        {
            // Run synchronously since we're in an IEnumerable method
            var languageCode = settings.GetLanguageCode(entry.SourceCode);
            var correctionTask = grammarCorrector.AutoCorrectAsync(entry.Definition, languageCode);
            correctionTask.Wait(); // Caution: this blocks

            if (correctionTask.IsCompletedSuccessfully &&
                correctionTask.Result.AppliedCorrections.Any())
            {
                logger.LogDebug(
                    "Applied grammar correction to '{Word}' from {Source}",
                    entry.Word, entry.SourceCode);

                return new DictionaryEntry
                {
                    Word = entry.Word,
                    NormalizedWord = entry.NormalizedWord,
                    PartOfSpeech = entry.PartOfSpeech,
                    Definition = correctionTask.Result.CorrectedText,
                    Etymology = entry.Etymology,
                    SenseNumber = entry.SenseNumber,
                    SourceCode = entry.SourceCode,
                    CreatedUtc = entry.CreatedUtc
                };
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Grammar correction failed for word '{Word}' from source {Source}",
                entry.Word, entry.SourceCode);
        }

        return entry;
    }
}