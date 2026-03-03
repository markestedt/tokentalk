namespace TokenTalk.PostProcessing;

public class DictionaryProcessor : IPostProcessor
{
    private readonly CustomDictionary _dictionary;

    public DictionaryProcessor(CustomDictionary dictionary)
    {
        _dictionary = dictionary;
    }

    public Task<string> ProcessAsync(string text, CancellationToken ct = default)
    {
        var result = text;
        foreach (var (original, replacement) in _dictionary.GetMappings())
        {
            result = ReplaceInsensitive(result, original, replacement);
        }
        return Task.FromResult(result);
    }

    private static string ReplaceInsensitive(string text, string original, string replacement)
    {
        if (string.IsNullOrEmpty(original))
            return text;

        var lowerText = text.ToLower();
        var lowerOriginal = original.ToLower();
        var sb = new System.Text.StringBuilder();
        int startPos = 0;

        while (true)
        {
            int index = lowerText.IndexOf(lowerOriginal, startPos, StringComparison.Ordinal);
            if (index == -1)
            {
                sb.Append(text[startPos..]);
                break;
            }

            sb.Append(text[startPos..index]);
            sb.Append(replacement);
            startPos = index + original.Length;
        }

        return sb.ToString();
    }
}
