namespace DictionaryImporter.Gateway.Rewriter;

public interface ITitleCaseProcessor
{
    TitleCaseResult NormalizeTitleSafe(string input);
    bool ShouldPreserveToken(string token);
    void ReloadConfiguration();
}