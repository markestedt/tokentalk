using System.Text.Json.Serialization;

namespace TokenTalk.PostProcessing;

public class DictionaryEntry
{
    [JsonPropertyName("original")]
    public string Original { get; set; } = string.Empty;

    [JsonPropertyName("replacement")]
    public string Replacement { get; set; } = string.Empty;

    [JsonPropertyName("isMapping")]
    public bool IsMapping { get; set; }
}

public class CustomDictionary
{
    public List<DictionaryEntry> Entries { get; set; } = [];

    public IEnumerable<string> GetSimpleTerms() =>
        Entries.Where(e => !e.IsMapping).Select(e => e.Replacement);

    public IEnumerable<(string Original, string Replacement)> GetMappings() =>
        Entries.Where(e => e.IsMapping).Select(e => (e.Original, e.Replacement));
}
