internal static class CanonicalEligibility
{
    public static bool IsEligible(string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        // Must be a single lexical token
        if (normalized.Contains(' '))
            return false;

        // Reject apostrophes and possessives
        if (normalized.Contains('\''))
            return false;

        return true;
    }
}
