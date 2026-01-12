namespace DictionaryImporter.Infrastructure.Qa;

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
        }
        // Add more QA SPs here as system grows
    }
}