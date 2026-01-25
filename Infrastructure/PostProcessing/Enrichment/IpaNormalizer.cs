namespace DictionaryImporter.Infrastructure.PostProcessing.Enrichment;

internal static class IpaNormalizer
{
    public static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;

        var ipa = raw.Trim();

        if (ipa.StartsWith("/") && ipa.EndsWith("/"))
            ipa = ipa.Substring(1, ipa.Length - 2);

        return ipa.Trim();
    }
}