using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Line.OpenApi.Messaging;
using Microsoft.Extensions.AI;
using Microsoft.Kiota.Abstractions.Authentication;

namespace Line.OpenApi.Extensions.AI.Tests;

internal static class TestSupport
{
    /// <summary>A single text message payload usable by the send tools.</summary>
    public const string OneText = "[{\"type\":\"text\",\"text\":\"hi\"}]";

    /// <summary>Builds a MessagingClient whose transport is the given handler (no real network).</summary>
    public static MessagingClient NewClient(HttpMessageHandler handler)
        => new(new AnonymousAuthenticationProvider(), new HttpClient(handler));

    /// <summary>The JSON-schema property names of a tool's arguments (empty when it takes none).</summary>
    public static IReadOnlyCollection<string> SchemaPropertyNames(this AIFunction function)
    {
        if (function.JsonSchema.ValueKind == JsonValueKind.Object &&
            function.JsonSchema.TryGetProperty("properties", out var props) &&
            props.ValueKind == JsonValueKind.Object)
        {
            return props.EnumerateObject().Select(p => p.Name).ToList();
        }
        return System.Array.Empty<string>();
    }

    /// <summary>Transport that fails if it is ever called — proves a code path never sends.</summary>
    public sealed class ExplodingHandler : HttpMessageHandler
    {
        public bool WasCalled { get; private set; }

        protected override System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new System.InvalidOperationException(
                $"Transport was touched ({request.Method} {request.RequestUri}) but the code path must not send.");
        }
    }

    /// <summary>Transport that records the request and returns a canned response.</summary>
    public sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public RecordingHandler(HttpResponseMessage? response = null)
            => _response = response ?? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };

        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }
        public int CallCount { get; private set; }

        protected override async System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            CallCount++;
            Request = request;
            if (request.Content is not null)
                RequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return _response;
        }
    }
}
