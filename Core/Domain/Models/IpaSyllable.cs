namespace DictionaryImporter.Core.Domain.Models;

/// <summary>
///     Represents a derived IPA syllable.
///     StressLevel: 0 = none, 1 = secondary, 2 = primary
/// </summary>
internal sealed record IpaSyllable(
    int Index,
    string Text,
    byte StressLevel
);