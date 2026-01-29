namespace DictionaryImporter.Gateway.Rewriter;

public sealed record AbbreviationStandardizeResult(
    bool Changed,
    string Text,
    IReadOnlyList<string> AppliedKeys)
{
    public static AbbreviationStandardizeResult NoChange(string text)
        => new(false, text ?? string.Empty, Array.Empty<string>());
}