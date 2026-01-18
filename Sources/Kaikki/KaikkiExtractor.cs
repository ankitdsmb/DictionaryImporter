namespace DictionaryImporter.Sources.Kaikki
{
    public sealed class KaikkiExtractor : IDataExtractor<KaikkiRawEntry>
    {
        private readonly ILogger<KaikkiExtractor> _logger;

        public KaikkiExtractor(ILogger<KaikkiExtractor> logger)
        {
            _logger = logger;
        }

        public async IAsyncEnumerable<KaikkiRawEntry> ExtractAsync(
            Stream stream,
            [EnumeratorCancellation] CancellationToken ct)
        {
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                yield return new KaikkiRawEntry { RawJson = line };
            }
        }
    }
}