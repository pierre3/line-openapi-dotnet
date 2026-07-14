using System.Net;

namespace Line.OpenApi.Tools.Tests;

/// <summary>Test handler that returns a canned response, capturing the last request.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string? _jsonBody;

    public StubHttpMessageHandler(HttpStatusCode status, string? jsonBody = null)
    {
        _status = status;
        _jsonBody = jsonBody;
    }

    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        var response = new HttpResponseMessage(_status);
        if (_jsonBody is not null)
        {
            response.Content = new StringContent(_jsonBody, System.Text.Encoding.UTF8, "application/json");
        }

        return Task.FromResult(response);
    }
}
