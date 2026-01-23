using DictionaryImporter.Common;

namespace DictionaryImporter.Sources.Kaikki
{
    public sealed class KaikkiExtractor(ILogger<KaikkiExtractor> logger)
        : IDataExtractor<KaikkiRawEntry>
    {
        private const string SourceCode = "KAIKKI";

        private readonly ILogger<KaikkiExtractor> _logger = logger;

        public async IAsyncEnumerable<KaikkiRawEntry> ExtractAsync(
            Stream stream,
            [EnumeratorCancellation] CancellationToken ct)
        {
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync() is { } line)
            {
                ct.ThrowIfCancellationRequested();

                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                if (!Helper.ShouldContinueProcessing(SourceCode, _logger))
                    yield break;

                yield return new KaikkiRawEntry { RawJson = trimmed };
            }
        }
    }
}