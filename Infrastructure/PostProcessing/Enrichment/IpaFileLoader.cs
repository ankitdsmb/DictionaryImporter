namespace DictionaryImporter.Infrastructure.PostProcessing.Enrichment
{
    internal static class IpaFileLoader
    {
        public static IEnumerable<(string Word, string Ipa)> Load(string path)
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split('\t');
                if (parts.Length < 2)
                    continue;

                var word = parts[0].Trim().ToLowerInvariant();
                var ipa =
                    IpaNormalizer.Normalize(
                        parts[1].Split(',')[0]);

                yield return (word, ipa);
            }
        }
    }
}