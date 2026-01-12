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
                name: "IPA Quality & Coverage",
                phase: "IPA",
                procedureName: "dbo.qa_VerifyIpaQualityAndCoverage",
                 connectionString: connectionString,
                        parameters: new { LocaleCode = locale }
                );

                yield return new QaStoredProcedureCheck(
                    name: "IPA ↔ Syllable Alignment",
                    phase: "Syllables",
                    procedureName: "dbo.qa_VerifyOrthographicSyllables",
                    connectionString: connectionString,
                        parameters: new { LocaleCode = locale }
                    );
            }
            // Add more QA SPs here as system grows
        }
    }
}
