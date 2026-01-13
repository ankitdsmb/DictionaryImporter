// File: TestGrammarCorrection.cs

using DictionaryImporter.Core.Grammar;
using DictionaryImporter.Core.Grammar.Simple;

public class GrammarTest
{
    public static async Task TestSimpleGrammar()
    {
        //var corrector = new SimpleGrammarCorrector("http://localhost:2026");

        //var testText = "He go to the store yesturday.";

        //Console.WriteLine($"Testing: {testText}");

        //// Check for issues
        //var checkResult = await corrector.CheckAsync(testText);
        //Console.WriteLine($"Has issues: {checkResult.HasIssues}");
        //Console.WriteLine($"Issue count: {checkResult.IssueCount}");

        //// Auto-correct
        //var correctionResult = await corrector.AutoCorrectAsync(testText);
        //Console.WriteLine($"Original: {correctionResult.OriginalText}");
        //Console.WriteLine($"Corrected: {correctionResult.CorrectedText}");
        //Console.WriteLine($"Applied corrections: {correctionResult.AppliedCorrections.Count}");

        //// Get suggestions
        //var suggestions = await corrector.SuggestImprovementsAsync(testText);
        //Console.WriteLine($"Suggestions: {suggestions.Count}");
    }
}