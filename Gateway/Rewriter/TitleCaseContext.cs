namespace DictionaryImporter.Gateway.Rewriter;

internal sealed class TitleCaseContext
{
    public bool InParentheses { get; private set; }
    public bool InQuotes { get; private set; }
    public bool AfterColon { get; private set; }

    public void Update(string token)
    {
        switch (token)
        {
            case "(":
                InParentheses = true;
                break;
            case ")":
                InParentheses = false;
                break;
            case "\"":
                InQuotes = !InQuotes;
                break;
            case ":":
                AfterColon = true;
                break;
            default:
                AfterColon = false;
                break;
        }
    }
}