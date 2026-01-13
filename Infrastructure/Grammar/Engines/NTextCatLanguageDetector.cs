// File: DictionaryImporter.Infrastructure/Grammar/Engines/NTextCatLanguageDetector.cs
using DictionaryImporter.Core.Grammar;
using DictionaryImporter.Core.Grammar.Enhanced;
using NTextCat;
using System.Diagnostics;

namespace DictionaryImporter.Infrastructure.Grammar.Engines;

public sealed class NTextCatLanguageDetector(string profilePath, ILogger<NTextCatLanguageDetector> logger) : IGrammarEngine
{
    private RankedLanguageIdentifier? _identifier;

    public string Name => "NTextCat";
    public double ConfidenceWeight => 0.80;

    public bool IsSupported(string languageCode) => true;

    public Task InitializeAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                if (File.Exists(profilePath))
                {
                    var factory = new RankedLanguageIdentifierFactory();
                    _identifier = factory.Load(profilePath);
                    logger?.LogInformation("NTextCat initialized successfully");
                }
                else
                {
                    logger?.LogWarning("NTextCat profile not found at {Path}", profilePath);
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to initialize NTextCat");
            }
        });
    }

    public Task<GrammarCheckResult> CheckAsync(string text, string languageCode, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var issues = new List<GrammarIssue>();

        if (_identifier == null || string.IsNullOrWhiteSpace(text) || text.Length < 10)
        {
            return Task.FromResult(new GrammarCheckResult(false, 0, issues, sw.Elapsed));
        }

        try
        {
            var languages = _identifier.Identify(text);
            var detected = languages.FirstOrDefault();

            if (detected != null && !IsLanguageMatch(detected.Item1.Iso639_3, languageCode))
            {
                issues.Add(new GrammarIssue(
                    $"LANGUAGE_MISMATCH_{detected.Item1.Iso639_3}",
                    $"Text appears to be in {GetLanguageName(detected.Item1.Iso639_3)} (confidence: {detected.Item2:P0}), not {languageCode}",
                    "LANGUAGE",
                    0,
                    text.Length,
                    new List<string> { $"Consider setting language to {detected.Item1.Iso639_3}" },
                    (int)(detected.Item2 * 100)
                ));
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "NTextCat language detection failed");
        }

        sw.Stop();
        return Task.FromResult(new GrammarCheckResult(
            issues.Any(),
            issues.Count,
            issues,
            sw.Elapsed
        ));
    }

    public Task<GrammarCorrectionResult> AutoCorrectAsync(string text, string languageCode, CancellationToken ct)
    {
        // NTextCat only detects language, doesn't correct
        return Task.FromResult(new GrammarCorrectionResult(
            text,
            text,
            Array.Empty<AppliedCorrection>(),
            Array.Empty<GrammarIssue>()
        ));
    }

    private bool IsLanguageMatch(string iso639, string languageCode)
    {
        var mapping = new Dictionary<string, string[]>
        {
            ["eng"] = new[] { "en", "en-US", "en-GB", "en-CA", "en-AU" },
            ["fra"] = new[] { "fr", "fr-FR", "fr-CA" },
            ["deu"] = new[] { "de", "de-DE", "de-AT", "de-CH" },
            ["spa"] = new[] { "es", "es-ES", "es-MX" },
            ["ita"] = new[] { "it", "it-IT" },
            ["por"] = new[] { "pt", "pt-PT", "pt-BR" },
            ["rus"] = new[] { "ru", "ru-RU" },
            ["zho"] = new[] { "zh", "zh-CN", "zh-TW" },
            ["jpn"] = new[] { "ja", "ja-JP" },
            ["kor"] = new[] { "ko", "ko-KR" }
        };

        return mapping.TryGetValue(iso639, out var codes) &&
               codes.Any(c => languageCode.StartsWith(c, StringComparison.OrdinalIgnoreCase));
    }

    private string GetLanguageName(string iso639)
    {
        var names = new Dictionary<string, string>
        {
            ["eng"] = "English",
            ["fra"] = "French",
            ["deu"] = "German",
            ["spa"] = "Spanish",
            ["ita"] = "Italian",
            ["por"] = "Portuguese",
            ["rus"] = "Russian",
            ["zho"] = "Chinese",
            ["jpn"] = "Japanese",
            ["kor"] = "Korean"
        };

        return names.TryGetValue(iso639, out var name) ? name : iso639;
    }
}