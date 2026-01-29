namespace DictionaryImporter.Gateway.Rewriter;

public static class BracketQuoteBalancer
{
    public sealed class BalanceResult(string text, bool changed, string? reason)
    {
        public string Text { get; } = text;
        public bool Changed { get; } = changed;
        public string? Reason { get; } = reason;
    }

    public static BalanceResult BalanceSafe(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new BalanceResult(input, false, null);

        try
        {
            var s = input;

            // 1) Parentheses balance: only fix if exactly one missing and it's safe
            s = FixSingleMissingPairSafe(s, '(', ')', out var parenChanged);

            // 2) Square brackets balance: same safe logic
            s = FixSingleMissingPairSafe(s, '[', ']', out var bracketChanged);

            // 3) Curly braces balance: same safe logic
            s = FixSingleMissingPairSafe(s, '{', '}', out var braceChanged);

            // 4) Straight double quote balance: only if exactly one quote exists
            var quoteChanged = false;
            var quoteCount = CountChar(s, '"');
            if (quoteCount == 1)
            {
                // safest: append closing quote if last char isn't quote already
                s = s.EndsWith('"') ? s : s + "\"";
                quoteChanged = !string.Equals(s, input, StringComparison.Ordinal);
            }

            // 5) Smart quotes balance (very conservative)
            var smartChanged = false;
            var openSmart = CountChar(s, '“');
            var closeSmart = CountChar(s, '”');
            if (openSmart == 1 && closeSmart == 0)
            {
                s += "”";
                smartChanged = true;
            }
            else if (openSmart == 0 && closeSmart == 1)
            {
                // remove only if it's a trailing closing smart quote
                if (s.EndsWith("”", StringComparison.Ordinal))
                {
                    s = s.Substring(0, s.Length - 1);
                    smartChanged = true;
                }
            }

            var changed = parenChanged || bracketChanged || braceChanged || quoteChanged || smartChanged;
            if (!changed)
                return new BalanceResult(input, false, null);

            // Final trim only (safe)
            s = s.Trim();

            return new BalanceResult(s, true, "Balanced trivial brackets/quotes safely.");
        }
        catch
        {
            return new BalanceResult(input, false, null);
        }
    }

    private static string FixSingleMissingPairSafe(
        string input,
        char open,
        char close,
        out bool changed)
    {
        changed = false;

        var openCount = CountChar(input, open);
        var closeCount = CountChar(input, close);

        if (openCount == closeCount)
            return input;

        // Only fix if the imbalance is exactly 1
        if (Math.Abs(openCount - closeCount) != 1)
            return input;

        // If we have one extra opening, append closing
        if (openCount == closeCount + 1)
        {
            var result = input + close;
            changed = true;
            return result;
        }

        // If we have one extra closing:
        // safest is removing ONLY if closing is trailing
        if (closeCount == openCount + 1)
        {
            if (input.Length > 0 && input[^1] == close)
            {
                var result = input.Substring(0, input.Length - 1);
                changed = true;
                return result;
            }

            return input;
        }

        return input;
    }

    private static int CountChar(string s, char ch)
    {
        var count = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == ch)
                count++;
        }
        return count;
    }
}