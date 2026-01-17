using DictionaryImporter.Gateway.Grammar.Correctors;
using Microsoft.Extensions.Logging.Abstractions;

namespace DictionaryImporter.Infrastructure.Qa
{
    public static class KnownQaChecks
    {
        public static IEnumerable<IQaCheck> CreateAll(
            string connectionString)
        {
            foreach (var locale in new[] { "en", "en-UK", "en-US" })
            {
                yield return new QaStoredProcedureCheck(
                    "IPA Quality & Coverage",
                    "IPA",
                    "dbo.qa_VerifyIpaQualityAndCoverage",
                    connectionString,
                    new { LocaleCode = locale }
                );

                yield return new QaStoredProcedureCheck(
                    "IPA ↔ Syllable Alignment",
                    "Syllables",
                    "dbo.qa_VerifyOrthographicSyllables",
                    connectionString,
                    new { LocaleCode = locale }
                );

                var grammarEnabled = Environment.GetEnvironmentVariable("GRAMMAR_QA_ENABLED") == "true";
                if (grammarEnabled)
                {
                    yield return new GrammarQaCheck(connectionString,
                        new LanguageToolGrammarCorrector(),
                        NullLogger<GrammarQaCheck>.Instance);
                }
            }
        }
    }
}