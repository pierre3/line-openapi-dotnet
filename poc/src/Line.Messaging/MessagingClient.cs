using System;
using System.Net.Http;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Line.Core.Authentication;
using Line.Messaging.Generated.Api;   // control plane (api.line.me)   * generated after code generation
using Line.Messaging.Generated.Blob;  // data plane (api-data.line.me) * generated after code generation

namespace Line.Messaging;

/// <summary>
/// Facade for the Messaging API. It unifies the two Kiota clients for the control plane
/// (api.line.me) and the data plane (api-data.line.me) so callers do not have to think about
/// the host difference.
///
/// Usage:
///   var line = MessagingClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
///   await line.Api.V2.Bot.Message.Push.PostAsync(pushRequest);         // send (control plane)
///   var stream = await line.Blob.V2.Bot.Message[messageId].Content.GetAsync(); // fetch (data plane)
/// </summary>
public sealed class MessagingClient
{
    /// <summary>Control-plane client (send, reply, rich-menu operations, etc.; api.line.me).</summary>
    public MessagingApiClient Api { get; }

    /// <summary>Data-plane client (content download, image upload, etc.; api-data.line.me).</summary>
    public MessagingBlobApiClient Blob { get; }

    /// <param name="authProvider">Authentication provider (static or refreshing, either works).</param>
    /// <param name="httpClient">
    /// The <see cref="HttpClient"/> shared by the two adapters (control plane and data plane).
    /// Supplied by <c>IHttpClientFactory</c> via DI (shared handler pool, Kiota default
    /// middleware applied). When null, each adapter creates its own default
    /// <see cref="HttpClient"/> (for PoC/quick use).
    /// Since the adapters build the URLs themselves, <see cref="HttpClient.BaseAddress"/> is
    /// not used, so sharing one is fine.
    /// </param>
    public MessagingClient(IAuthenticationProvider authProvider, HttpClient? httpClient = null)
    {
        if (authProvider is null) throw new ArgumentNullException(nameof(authProvider));

        // Control plane: use the spec's server (api.line.me) as-is.
        var apiAdapter = new HttpClientRequestAdapter(authProvider, httpClient: httpClient);
        Api = new MessagingApiClient(apiAdapter);

        // Data plane: use a separate adapter and explicitly set BaseUrl to api-data.line.me.
        // (Even with separate generation, the root server stays api.line.me, so this override
        // is mandatory.)
        // Important: the generated client fixes baseurl into PathParameters in its constructor
        // (defaulting to api.line.me when empty). Therefore BaseUrl must be set *before*
        // construction. Setting it afterward is not reflected into PathParameters, and requests
        // would go to api.line.me.
        var blobAdapter = new HttpClientRequestAdapter(authProvider, httpClient: httpClient);
        blobAdapter.BaseUrl = $"https://{LineHosts.ApiData}";
        Blob = new MessagingBlobApiClient(blobAdapter);
    }

    /// <summary>Helper to quickly construct from a long-lived channel access token.</summary>
    public static MessagingClient CreateWithStaticToken(string channelAccessToken)
    {
        var provider = new StaticChannelAccessTokenProvider(channelAccessToken);
        var auth = new BaseBearerTokenAuthenticationProvider(provider);
        return new MessagingClient(auth);
    }
}
