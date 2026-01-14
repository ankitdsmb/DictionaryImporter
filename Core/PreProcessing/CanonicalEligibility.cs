namespace DictionaryImporter.Core.PreProcessing;

internal static class CanonicalEligibility
{
    public static bool IsEligible(string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (normalized.Contains(' '))
            return false;

        if (normalized.Contains('\''))
            return false;

        return true;
    }
}