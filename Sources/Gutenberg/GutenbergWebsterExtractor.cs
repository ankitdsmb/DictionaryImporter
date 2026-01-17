namespace DictionaryImporter.Sources.Gutenberg
{
    public sealed class GutenbergWebsterExtractor(ILogger<GutenbergWebsterExtractor> logger)
        : IDataExtractor<GutenbergRawEntry>
    {
        public async IAsyncEnumerable<GutenbergRawEntry> ExtractAsync(
            Stream source,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            logger.LogInformation("Gutenberg extraction started");

            using var reader = new StreamReader(
                source,
                Encoding.UTF8,
                false,
                16 * 1024,
                true);

            string? line;
            var bodyStarted = false;
            GutenbergRawEntry? current = null;
            long count = 0;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!bodyStarted)
                {
                    if (line.StartsWith("*** START"))
                    {
                        bodyStarted = true;
                        logger.LogInformation("Gutenberg body detected");
                    }

                    continue;
                }

                if (line.StartsWith("*** END"))
                    break;

                if (IsHeadword(line))
                {
                    if (current != null)
                    {
                        yield return current;
                        count++;
                    }

                    current = new GutenbergRawEntry
                    {
                        Headword = line.Trim()
                    };
                    continue;
                }

                current?.Lines.Add(line);
            }

            if (current != null)
            {
                yield return current;
                count++;
            }

            logger.LogInformation(
                "Gutenberg extraction completed. Entries: {Count}",
                count);
        }

        private static bool IsHeadword(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var text = line.Trim();

            if (text.Length > 40)
                return false;

            if (!text.Equals(text.ToUpperInvariant(), StringComparison.Ordinal))
                return false;

            var hasLetter = text.Any(char.IsLetter);
            if (!hasLetter)
                return false;

            return true;
        }
    }
}