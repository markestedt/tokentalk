using Microsoft.Extensions.Logging;

namespace TokenTalk.PostProcessing;

public class PostProcessingPipeline
{
    private readonly List<IPostProcessor> _processors = [];
    private readonly ILogger<PostProcessingPipeline> _logger;

    public PostProcessingPipeline(ILogger<PostProcessingPipeline> logger)
    {
        _logger = logger;
    }

    public void AddProcessor(IPostProcessor processor)
    {
        _processors.Add(processor);
    }

    public async Task<string> ProcessAsync(string text, CancellationToken ct = default)
    {
        var result = text;
        foreach (var processor in _processors)
        {
            try
            {
                result = await processor.ProcessAsync(result, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Post-processor {Processor} failed, continuing with previous text",
                    processor.GetType().Name);
            }
        }
        return result;
    }
}
