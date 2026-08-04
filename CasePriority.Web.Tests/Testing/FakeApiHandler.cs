using System.Net;

namespace CasePriority.Web.Tests.Testing;

/// <summary>
/// Stands in for the real API so the web tests are fast and DB-free. Tests set a
/// responder; the handler records the last request for header/URL/body asserts.
/// </summary>
public sealed class FakeApiHandler : HttpMessageHandler
{
    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder =
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

    public HttpRequestMessage? LastRequest { get; private set; }

    public void RespondWith(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _responder = (request, _) => Task.FromResult(responder(request));

    public void RespondWith(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) =>
        _responder = responder;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return await _responder(request, cancellationToken);
    }

    // Shared across the factory lifetime — do not dispose from the HttpClient.
    protected override void Dispose(bool disposing)
    {
    }
}
