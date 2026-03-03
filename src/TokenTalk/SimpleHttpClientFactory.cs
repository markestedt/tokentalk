using System.Net.Http;

namespace TokenTalk;

/// <summary>
/// Simple IHttpClientFactory implementation that creates clients with a shared handler.
/// Used to avoid the ASP0000 warning from calling BuildServiceProvider in Program.cs.
/// </summary>
internal sealed class SimpleHttpClientFactory : IHttpClientFactory
{
    private readonly TimeSpan _timeout;

    public SimpleHttpClientFactory(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    public HttpClient CreateClient(string name)
    {
        return new HttpClient { Timeout = _timeout };
    }
}
