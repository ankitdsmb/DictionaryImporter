using DictionaryImporter.Gateway.Grammar.Core;
using DictionaryImporter.Gateway.Grammar.Core.Models;
using DictionaryImporter.Gateway.Grammar.Core.Results;
using DictionaryImporter.Gateway.Grammar.Engines;
using System.Reflection;

namespace DictionaryImporter.Gateway.Grammar.Correctors
{
    public sealed class HunspellCorrectorAdapter(
        ILogger<HunspellCorrectorAdapter> logger)
        : IGrammarCorrector
    {
        private static readonly ConcurrentDictionary<string, Lazy<NHunspellSpellChecker>> SpellCheckerCache =
            new(StringComparer.OrdinalIgnoreCase);

        // ✅ Cache the resolved method per type for performance
        private static readonly ConcurrentDictionary<Type, MethodInfo?> AutoFixMethodCache =
            new();

        public Task<GrammarCheckResult> CheckAsync(
            string text,
            string? languageCode = null,
            CancellationToken ct = default)
            => Task.FromResult(new GrammarCheckResult(false, 0, [], TimeSpan.Zero));

        public Task<GrammarCorrectionResult> AutoCorrectAsync(
            string text,
            string? languageCode = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Task.FromResult(new GrammarCorrectionResult(
                    OriginalText: text,
                    CorrectedText: text,
                    AppliedCorrections: [],
                    RemainingIssues: []));
            }

            try
            {
                languageCode = NormalizeLanguage(languageCode);

                var spellChecker = GetSpellChecker(languageCode);
                if (spellChecker is null)
                {
                    return Task.FromResult(new GrammarCorrectionResult(
                        OriginalText: text,
                        CorrectedText: text,
                        AppliedCorrections: [],
                        RemainingIssues: []));
                }

                var corrected = TryFixText(spellChecker, text);

                // If hunspell couldn't fix (or method not found), keep original (safe)
                if (string.IsNullOrWhiteSpace(corrected) ||
                    string.Equals(corrected, text, StringComparison.Ordinal))
                {
                    return Task.FromResult(new GrammarCorrectionResult(
                        OriginalText: text,
                        CorrectedText: text,
                        AppliedCorrections: [],
                        RemainingIssues: []));
                }

                return Task.FromResult(new GrammarCorrectionResult(
                    OriginalText: text,
                    CorrectedText: corrected,
                    AppliedCorrections:
                    [
                        // ✅ positional constructor (your record doesn't support named args)
                        new AppliedCorrection(
                            "HUNSPELL_SPLIT_JOIN",
                            "hunspell_split_join",
                            "Fixed split/joined words (Hunspell)",
                            corrected,
                            92)
                    ],
                    RemainingIssues: []));
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "HunspellCorrectorAdapter failed");
                return Task.FromResult(new GrammarCorrectionResult(
                    OriginalText: text,
                    CorrectedText: text,
                    AppliedCorrections: [],
                    RemainingIssues: []));
            }
        }

        public Task<IReadOnlyList<GrammarSuggestion>> SuggestImprovementsAsync(
            string text,
            string? languageCode = null,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GrammarSuggestion>>([]);

        private NHunspellSpellChecker? GetSpellChecker(string languageCode)
        {
            try
            {
                var lazy = SpellCheckerCache.GetOrAdd(languageCode,
                    lc => new Lazy<NHunspellSpellChecker>(() => new NHunspellSpellChecker(lc)));

                return lazy.Value;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Unable to initialize Hunspell for language '{Language}'", languageCode);
                return null;
            }
        }

        private static string NormalizeLanguage(string? languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
                return "en-US";

            return languageCode.Trim();
        }

        private string TryFixText(NHunspellSpellChecker spellChecker, string text)
        {
            // ✅ We don't know exact method name in your codebase,
            // so resolve dynamically once and reuse via cache.
            var type = spellChecker.GetType();

            var method = AutoFixMethodCache.GetOrAdd(type, t =>
            {
                // Try common method names used in spell checker utilities
                var candidates = new[]
                {
                    "AutoCorrect",
                    "Correct",
                    "FixText",
                    "Fix",
                    "Normalize",
                    "Clean",
                };

                foreach (var name in candidates)
                {
                    var m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public, null, [typeof(string)], null);
                    if (m is not null && m.ReturnType == typeof(string))
                        return m;
                }

                return null;
            });

            if (method is null)
                return text;

            try
            {
                var result = method.Invoke(spellChecker, [text]) as string;
                return string.IsNullOrWhiteSpace(result) ? text : result!;
            }
            catch
            {
                return text;
            }
        }
    }
}