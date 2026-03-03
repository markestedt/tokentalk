using System.Net.Http.Headers;
using System.Text.Json;
using TokenTalk.Audio;

namespace TokenTalk.Transcription;

public class OpenAiWhisperProvider : ITranscriptionProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Func<string> _getApiKey;
    private readonly Func<string> _getModel;
    private readonly Func<string> _getLanguage;
    private readonly Func<string> _getPrompt;
    private readonly IEnumerable<string> _dictionaryTerms;

    public string Name => "openai";

    public OpenAiWhisperProvider(
        IHttpClientFactory httpClientFactory,
        Func<string> getApiKey,
        Func<string> getModel,
        Func<string> getLanguage,
        Func<string> getPrompt,
        IEnumerable<string> dictionaryTerms)
    {
        _httpClientFactory = httpClientFactory;
        _getApiKey = getApiKey;
        _getModel = getModel;
        _getLanguage = getLanguage;
        _getPrompt = getPrompt;
        _dictionaryTerms = dictionaryTerms;
    }

    public async Task<string> TranscribeAsync(AudioSegment audio, CancellationToken ct = default)
    {
        var apiKey = _getApiKey();
        var model = _getModel();
        var language = _getLanguage();
        var prompt = _getPrompt();

        var httpClient = _httpClientFactory.CreateClient("OpenAI");
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);

        using var content = new MultipartFormDataContent();

        // Add audio file
        var audioContent = new ByteArrayContent(audio.WavData);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audioContent, "file", "audio.wav");

        // Add model
        content.Add(new StringContent(model), "model");

        // Add language ("auto" or empty = omit parameter, Whisper auto-detects)
        if (!string.IsNullOrEmpty(language) && language != "auto")
            content.Add(new StringContent(language), "language");

        // Build prompt: user prompt + dictionary simple terms
        var promptParts = new List<string>();
        if (!string.IsNullOrEmpty(prompt))
            promptParts.Add(prompt);
        promptParts.AddRange(_dictionaryTerms);

        if (promptParts.Count > 0)
            content.Add(new StringContent(string.Join(", ", promptParts)), "prompt");

        var response = await httpClient.PostAsync(
            "https://api.openai.com/v1/audio/transcriptions",
            content,
            ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Whisper API error ({response.StatusCode}): {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("text").GetString() ?? string.Empty;
    }
}
