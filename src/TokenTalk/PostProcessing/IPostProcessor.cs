namespace TokenTalk.PostProcessing;

public interface IPostProcessor
{
    Task<string> ProcessAsync(string text, CancellationToken ct = default);
}
