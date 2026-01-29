namespace DictionaryImporter.Gateway.Rewriter;

// Helper classes

// Main implementation class

// Static facade for backward compatibility
public static class MeaningTitleCasePreserver
{
    private static readonly TitleCasePreservationService _service =
        TitleCasePreservationService.Default;

    public static TitleCaseResult NormalizeTitleSafe(string input)
        => _service.NormalizeTitleSafe(input);

    public static bool ShouldPreserveToken(string token)
        => _service.ShouldPreserveToken(token);

    public static void ReloadConfiguration()
        => _service.ReloadConfiguration();
}