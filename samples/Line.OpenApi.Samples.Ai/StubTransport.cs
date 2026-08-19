using System.Net;
using System.Text;

namespace Line.OpenApi.Samples.Ai;

/// <summary>
/// A no-network HTTP handler for the offline demo: it answers every request with an empty 200 so the
/// Messaging client's send path completes locally. This lets the safety gates (SendPolicy /
/// BeforeSend) run for real — unlike dry-run, which would short-circuit before them — while never
/// contacting the LINE API.
/// </summary>
internal sealed class StubTransport : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });
}
