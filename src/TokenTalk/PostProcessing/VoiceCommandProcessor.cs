namespace TokenTalk.PostProcessing;

public class VoiceCommandProcessor : IPostProcessor
{
    private readonly Func<bool> _isEnabled;

    public VoiceCommandProcessor(Func<bool> isEnabled)
    {
        _isEnabled = isEnabled;
    }

    private static readonly (string Phrase, string Replacement)[] Commands =
    [
        ("new line", "\n"),
        ("newline", "\n"),
        ("new paragraph", "\n\n"),
        ("full stop", "."),
        ("dot", "."),
        ("comma", ","),
        ("question mark", "?"),
        ("exclamation mark", "!"),
        ("exclamation point", "!"),
        ("colon", ":"),
        ("semicolon", ";"),
        ("open quote", "\""),
        ("close quote", "\""),
        ("open parenthesis", "("),
        ("close parenthesis", ")"),
        ("open bracket", "["),
        ("close bracket", "]"),
        ("open brace", "{"),
        ("close brace", "}"),
        ("dash", "-"),
        ("underscore", "_"),
        ("slash", "/"),
        ("backslash", "\\"),
        ("at sign space", "@ "), // check before "at sign"
        ("at sign", "@"),
        ("at-sign", "@"),
        ("atsign", "@"),
        ("hash", "#"),
        ("dollar sign", "$"),
        ("percent sign", "%"),
        ("ampersand", "&"),
        ("asterisk", "*"),
        ("plus", "+"),
        ("equals", "="),
    ];

    public Task<string> ProcessAsync(string text, CancellationToken ct = default)
    {
        if (!_isEnabled())
            return Task.FromResult(text);

        var result = text;
        foreach (var (phrase, replacement) in Commands)
        {
            result = ReplaceWithWordBoundaries(result, phrase, replacement);
        }
        return Task.FromResult(result);
    }

    private static string ReplaceWithWordBoundaries(string text, string phrase, string replacement)
    {
        if (string.IsNullOrEmpty(phrase))
            return text;

        var lowerText = text.ToLower();
        var lowerPhrase = phrase.ToLower();
        var sb = new System.Text.StringBuilder();
        int lastIndex = 0;

        while (true)
        {
            int index = lowerText.IndexOf(lowerPhrase, lastIndex, StringComparison.Ordinal);
            if (index == -1)
            {
                sb.Append(text[lastIndex..]);
                break;
            }

            bool beforeIsBoundary = index == 0 || IsWordBoundary(text[index - 1]);
            int afterIndex = index + phrase.Length;
            bool afterIsBoundary = afterIndex >= text.Length || IsWordBoundary(text[afterIndex]);

            if (beforeIsBoundary && afterIsBoundary)
            {
                sb.Append(text[lastIndex..index]);
                sb.Append(replacement);
                lastIndex = afterIndex;
            }
            else
            {
                sb.Append(text[lastIndex..(index + 1)]);
                lastIndex = index + 1;
            }
        }

        return sb.ToString();
    }

    private static bool IsWordBoundary(char c)
    {
        return char.IsWhiteSpace(c) || char.IsPunctuation(c) || (!char.IsLetter(c) && !char.IsDigit(c));
    }
}
