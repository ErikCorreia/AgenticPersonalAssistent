using System.Text.RegularExpressions;

namespace Assistent.Speech;

public static partial class SpeechTextCleaner
{
    public static string Clean(string text, int maxChars = 1200)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var result = CodeBlock().Replace(text, " ");
        result = Url().Replace(result, " ");
        result = result.Replace("`", string.Empty);
        result = Markup().Replace(result, string.Empty);
        result = Heading().Replace(result, string.Empty);
        result = Spaces().Replace(result, " ").Trim();
        return result.Length > maxChars ? result[..maxChars] : result;
    }

    [GeneratedRegex(@"```[\s\S]*?```")] private static partial Regex CodeBlock();
    [GeneratedRegex(@"https?://\S+")] private static partial Regex Url();
    [GeneratedRegex(@"[*_#>|]+")] private static partial Regex Markup();
    [GeneratedRegex(@"^\s*[-+]\s+", RegexOptions.Multiline)] private static partial Regex Heading();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
}