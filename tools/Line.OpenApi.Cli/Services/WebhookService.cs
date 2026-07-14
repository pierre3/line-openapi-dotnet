using System.Net.Http;
using Line.OpenApi.Messaging.Webhook;

namespace Line.OpenApi.Cli.Services;

/// <summary>
/// C. Webhook development helpers. Signature verification and payload deserialization use the
/// library's <see cref="WebhookRequestParser"/> (self-contained, registry-independent). Replay
/// uses a bare <see cref="HttpClient"/> to a user-specified, non-LINE destination and therefore
/// does not apply the LINE AllowedHostsValidator (spec §4.3).
/// </summary>
public sealed class WebhookService
{
    /// <summary>
    /// Verifies a stored webhook payload's signature and deserializes it. Returns a summary of the
    /// events. Throws <see cref="WebhookSignatureException"/> / <see cref="WebhookPayloadException"/>.
    /// </summary>
    public async Task<WebhookParseResult> VerifyAsync(
        string channelSecret, byte[] body, string? signature, CancellationToken cancellationToken)
    {
        var callback = await WebhookRequestParser
            .ParseAsync(channelSecret, body, signature, cancellationToken)
            .ConfigureAwait(false);

        var events = callback.Events?
            .Select(e => DescribeEvent(e?.GetType().Name))
            .ToList() ?? new List<string>();

        return new WebhookParseResult(callback.Destination, events);
    }

    /// <summary>
    /// Replays a stored payload by POSTing the raw bytes to an arbitrary URL (e.g. a local app).
    /// No signature is added and the destination is not validated — this is a local dev aid.
    /// </summary>
    public async Task<ReplayResult> ReplayAsync(byte[] body, Uri target, CancellationToken cancellationToken)
    {
        using var http = new HttpClient();
        using var content = new ByteArrayContent(body);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using var response = await http.PostAsync(target, content, cancellationToken).ConfigureAwait(false);
        return new ReplayResult((int)response.StatusCode, response.ReasonPhrase);
    }

    // Generated event types are named e.g. "MessageEvent"; trim the suffix for a friendly label.
    private static string DescribeEvent(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            return "unknown";
        }

        return typeName.EndsWith("Event", StringComparison.Ordinal)
            ? typeName[..^"Event".Length]
            : typeName;
    }
}

/// <summary>Summary of a parsed webhook payload (non-secret).</summary>
public sealed record WebhookParseResult(string? Destination, IReadOnlyList<string> EventTypes);

/// <summary>Outcome of replaying a payload to a local destination.</summary>
public sealed record ReplayResult(int StatusCode, string? ReasonPhrase);
