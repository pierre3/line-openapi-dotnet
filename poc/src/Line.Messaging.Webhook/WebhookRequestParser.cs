using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Line.Core.Webhook;
using Line.Messaging.Webhook.Generated.Models;
using Microsoft.Kiota.Serialization.Json;

namespace Line.Messaging.Webhook;

/// <summary>
/// Entry point for receiving LINE webhooks. A thin helper that bundles <c>x-line-signature</c>
/// signature validation (<see cref="WebhookSignatureValidator"/>) and deserialization of the
/// body into <see cref="CallbackRequest"/> into a single call.
///
/// <para>
/// Deserialization is done by instantiating <see cref="JsonParseNodeFactory"/> directly and
/// does not depend at all on Kiota's global default serializer registry
/// (<c>ParseNodeFactoryRegistry.DefaultInstance</c> / <c>ApiClientBuilder.RegisterDefaultDeserializer</c>).
/// (<c>KiotaJsonSerializer</c> internally consults that default registry, so it fails in a
/// clean process where no JSON factory has been registered. Using the factory directly does
/// not carry that assumption.) As a result it works standalone even in an app that has not
/// constructed the Messaging client, and has no side effects (the regression is guarded by the
/// standalone assembly <c>Line.Messaging.Webhook.IsolationTests</c>).
/// </para>
/// <para>
/// Polymorphic reconstruction of the event array (dispatch to <see cref="MessageEvent"/> etc.
/// by the <c>type</c> discriminator, with a fallback to the base <see cref="Event"/> for
/// unknown types) is handled by the generated code. This helper only returns the
/// <see cref="CallbackRequest"/>; the subsequent event branching is done on the caller side
/// (see README).
/// </para>
///
/// Example usage with ASP.NET Core (obtaining the raw body and signature header is the
/// caller's responsibility):
/// <code>
///   var body = await ReadRawBodyBytesAsync(Request);          // raw bytes (same as what is signed)
///   var sig  = Request.Headers["x-line-signature"];
///   CallbackRequest callback = await parser.ParseAsync(body, sig);  // throws on invalid signature
/// </code>
/// </summary>
public sealed class WebhookRequestParser
{
    private readonly string _channelSecret;

    /// <param name="channelSecret">The channel secret (the key for signature validation).</param>
    /// <exception cref="ArgumentException"><paramref name="channelSecret"/> is empty or whitespace only.</exception>
    public WebhookRequestParser(string channelSecret)
    {
        // Match the DI-side Validate (IsNullOrWhiteSpace) so whitespace-only is rejected too.
        if (string.IsNullOrWhiteSpace(channelSecret))
            throw new ArgumentException("channel secret is required", nameof(channelSecret));
        _channelSecret = channelSecret;
    }

    /// <summary>
    /// Validates the signature with the channel secret supplied at construction and
    /// deserializes the body into <see cref="CallbackRequest"/>.
    /// </summary>
    /// <param name="body">The raw request body (both signature validation and deserialization operate on these same bytes).</param>
    /// <param name="signatureHeader">The <c>x-line-signature</c> header value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    /// <exception cref="WebhookSignatureException">Signature validation failed.</exception>
    /// <exception cref="WebhookPayloadException">The signature was valid but the body could not be deserialized.</exception>
    public Task<CallbackRequest> ParseAsync(
        byte[] body, string? signatureHeader, CancellationToken cancellationToken = default)
        => ParseAsync(_channelSecret, body, signatureHeader, cancellationToken);

    /// <summary>
    /// Validates the signature and deserializes the body, specifying the channel secret per
    /// call (for multi-tenant scenarios).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="body"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="channelSecret"/> is empty or whitespace only.</exception>
    /// <exception cref="WebhookSignatureException">Signature validation failed.</exception>
    /// <exception cref="WebhookPayloadException">The signature was valid but the body could not be deserialized.</exception>
    public static async Task<CallbackRequest> ParseAsync(
        string channelSecret, byte[] body, string? signatureHeader,
        CancellationToken cancellationToken = default)
    {
        if (body is null) throw new ArgumentNullException(nameof(body));

        // Signature validation runs synchronously on the raw bytes (WebhookSignatureValidator
        // rejects an empty secret).
        if (!WebhookSignatureValidator.IsValid(channelSecret, body, signatureHeader))
            throw new WebhookSignatureException("x-line-signature verification failed.");

        CallbackRequest? callback;
        try
        {
            // Deserialize the same bytes by instantiating the JSON factory directly, without
            // going through the default registry. This avoids depending on any global
            // deserializer registration (it works even in a clean process).
            using var stream = new MemoryStream(body, writable: false);
            var rootNode = await new JsonParseNodeFactory()
                .GetRootParseNodeAsync("application/json", stream, cancellationToken)
                .ConfigureAwait(false);
            // Polymorphic reconstruction (selecting the derived type of each event by its type
            // discriminator) is handled by the generated code.
            callback = rootNode.GetObjectValue(CallbackRequest.CreateFromDiscriminatorValue);
        }
        // Let cancellation propagate to the caller as-is (do not wrap it in PayloadException).
        catch (Exception ex) when (ex is not WebhookException && ex is not OperationCanceledException)
        {
            throw new WebhookPayloadException("Failed to deserialize webhook payload.", ex);
        }

        // Defensive guard (CallbackRequest.CreateFromDiscriminatorValue always returns an
        // instance, so this is unreachable for normal input, but guards against future
        // generation changes, empty streams, etc.).
        if (callback is null)
            throw new WebhookPayloadException("Webhook payload deserialized to null.");

        return callback;
    }
}
