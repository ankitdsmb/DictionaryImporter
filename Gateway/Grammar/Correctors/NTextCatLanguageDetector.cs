using DictionaryImporter.Gateway.Grammar.Core;
using DictionaryImporter.Gateway.Grammar.Core.Models;
using DictionaryImporter.Gateway.Grammar.Core.Results;

namespace DictionaryImporter.Gateway.Grammar.Correctors
{
    public sealed class NTextCatLanguageDetector(string profilePath, ILogger<NTextCatLanguageDetector> logger)
        : IGrammarEngine, IDisposable
    {
        private readonly object _lock = new();
        private bool _initialized = false;

        private dynamic? _identifier;

        private Type? _factoryType;
        private Type? _identifierType;

        public string Name => "NTextCat";
        public double ConfidenceWeight => 0.80;

        public bool IsSupported(string languageCode)
        {
            return true;
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
                        var ntextCatAssembly = AppDomain.CurrentDomain.GetAssemblies()
                            .FirstOrDefault(a => a.GetName().Name == "NTextCat");

                        if (ntextCatAssembly == null)
                        {
                            logger.LogWarning("NTextCat assembly not found. Language detection disabled.");
                            _initialized = true;
                            return;
                        }

                        _factoryType = ntextCatAssembly.GetType("Com.Lmax.Toolbag.NTextCat.RankedLanguageIdentifierFactory");
                        _identifierType = ntextCatAssembly.GetType("Com.Lmax.Toolbag.NTextCat.RankedLanguageIdentifier");

                        if (_factoryType == null || _identifierType == null)
                        {
                            logger.LogWarning("NTextCat types not found. Language detection disabled.");
                            _initialized = true;
                            return;
                        }

                        if (!File.Exists(profilePath))
                        {
                            logger.LogWarning("NTextCat profile not found at {Path}", profilePath);
                            _initialized = true;
                            return;
                        }

                        var factory = Activator.CreateInstance(_factoryType);
                        var loadMethod = _factoryType.GetMethod("Load");
                        _identifier = loadMethod?.Invoke(factory, [profilePath]);

                        _initialized = true;
                        logger.LogInformation("NTextCat language detector initialized successfully");
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to initialize NTextCat language detector");
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

            if (_identifier == null || string.IsNullOrWhiteSpace(text) || text.Length < 10)
            {
                return new GrammarCheckResult(false, 0, [], TimeSpan.Zero);
            }

            var sw = Stopwatch.StartNew();
            var issues = new List<GrammarIssue>();

            try
            {
                var identifyMethod = _identifierType?.GetMethod("Identify");
                if (identifyMethod == null)
                {
                    sw.Stop();
                    return new GrammarCheckResult(false, 0, [], sw.Elapsed);
                }

                var languages = identifyMethod.Invoke(_identifier, new object[] { text }) as IEnumerable<dynamic>;
                var mostCertainLanguage = languages?.FirstOrDefault();

                if (mostCertainLanguage != null)
                {
                    var detectedLanguageItem = mostCertainLanguage.Item1;
                    var confidence = (double)mostCertainLanguage.Item2;

                    var isoCodeProp = detectedLanguageItem.GetType().GetProperty("Iso639_3");
                    var detectedLanguage = isoCodeProp?.GetValue(detectedLanguageItem) as string ?? "unknown";

                    var expectedLang = MapToIso6393(languageCode);

                    if (!string.Equals(detectedLanguage, expectedLang, StringComparison.OrdinalIgnoreCase)
                        && confidence > 0.7)
                    {
                        var issue = new GrammarIssue(
                            StartOffset: 0,
                            EndOffset: Math.Min(50, text.Length),
                            Message: $"Text appears to be in '{detectedLanguage}' rather than expected '{expectedLang}' (confidence: {confidence:P0})",
                            ShortMessage: "Language Mismatch",
                            Replacements: new List<string>(), RuleId: $"LANGUAGE_{detectedLanguage.ToUpper()}",
                            RuleDescription: $"Language detected as {detectedLanguage}",
                            Tags: new List<string> { "language", "detection" },
                            Context: text.Substring(0, Math.Min(100, text.Length)),
                            ContextOffset: 0,
                            ConfidenceLevel: (int)(confidence * 100)
                        );

                        issues.Add(issue);
                    }
                }

                sw.Stop();
                return new GrammarCheckResult(true, issues.Count, issues, sw.Elapsed);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in NTextCat language detection");
                sw.Stop();
                return new GrammarCheckResult(false, 0, [], sw.Elapsed);
            }
        }

        private string MapToIso6393(string languageCode)
        {
            return languageCode.ToLower() switch
            {
                "en-us" => "eng",
                "en-gb" => "eng",
                "en" => "eng",
                "fr" => "fra",
                "de" => "deu",
                "es" => "spa",
                "it" => "ita",
                "pt" => "por",
                "ru" => "rus",
                "zh" => "zho",
                "ja" => "jpn",
                "ko" => "kor",
                _ => "eng"
            };
        }

        public void Dispose()
        {
            if (_identifier is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}