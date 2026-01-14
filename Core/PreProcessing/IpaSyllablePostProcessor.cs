namespace DictionaryImporter.Core.PreProcessing;

/// <summary>
///     Post-processor for IPA syllables.
///     Enforces linguistic invariants:
///     1. No vowel-only syllables
///     2. No consonant-only syllables
///     3. Stress preserved on merge
/// </summary>
internal static class IpaSyllablePostProcessor
{
    private static readonly Regex VowelRegex =
        new(@"[aeiouæɪʊəɐɑɔɛɜʌoøɒyɯɨɶ]", RegexOptions.Compiled);

    private static readonly Regex ConsonantRegex =
        new(@"[bcdfghjklmnpqrstvwxyzθðʃʒŋ]", RegexOptions.Compiled);

    /// <summary>
    ///     Normalizes syllables by merging invalid syllables
    ///     into their previous neighbor.
    /// </summary>
    public static IReadOnlyList<IpaSyllable> Normalize(
        IReadOnlyList<IpaSyllable> syllables)
    {
        if (syllables == null || syllables.Count == 0)
            return syllables;

        var buffer = new List<IpaSyllable>();

        foreach (var current in syllables)
        {
            if (buffer.Count == 0)
            {
                buffer.Add(current);
                continue;
            }

            var hasVowel = VowelRegex.IsMatch(current.Text);
            var hasConsonant = ConsonantRegex.IsMatch(current.Text);

            if (!hasVowel || !hasConsonant)
            {
                var prev = buffer[^1];

                buffer[^1] = new IpaSyllable(
                    prev.Index,
                    prev.Text + current.Text,
                    Math.Max(prev.StressLevel, current.StressLevel));
            }
            else
            {
                buffer.Add(current);
            }
        }

        var result = new List<IpaSyllable>(buffer.Count);
        var index = 1;

        foreach (var s in buffer)
            result.Add(
                new IpaSyllable(
                    index++,
                    s.Text,
                    s.StressLevel));

        return result;
    }
}