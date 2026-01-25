using DictionaryImporter.Common;
using System.Threading;
using System.Threading.Tasks;

namespace DictionaryImporter.Infrastructure.Persistence
{
    public sealed class SqlCanonicalWordPronunciationWriter(ISqlStoredProcedureExecutor sp)
    {
        private readonly ISqlStoredProcedureExecutor _sp = sp;

        public async Task WriteIfNotExistsAsync(
            long canonicalWordId,
            string localeCode,
            string ipa,
            CancellationToken ct)
        {
            if (canonicalWordId <= 0)
                return;

            var normalizedLocale = Helper.SqlRepository.NormalizeLocaleCodeOrNull(localeCode);
            if (string.IsNullOrWhiteSpace(normalizedLocale))
                return;

            var normalizedIpa = Helper.SqlRepository.NormalizeIpaOrNull(ipa);
            if (string.IsNullOrWhiteSpace(normalizedIpa))
                return;

            await Helper.SqlRepository.SafeExecuteAsync(async token =>
            {
                await _sp.ExecuteAsync(
                    "sp_CanonicalWordPronunciation_InsertIfMissing",
                    new
                    {
                        CanonicalWordId = canonicalWordId,
                        LocaleCode = normalizedLocale,
                        Ipa = normalizedIpa
                    },
                    token,
                    timeoutSeconds: 30);
            }, ct);
        }
    }
}