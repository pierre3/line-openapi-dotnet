using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Line.OpenApi.Core.Authentication;
using Line.OpenApi.Shop.Generated;
using Line.OpenApi.Shop.Generated.Models;

namespace Line.OpenApi.Shop;

/// <summary>
/// Facade for the Shop (mission sticker) API. It wraps a single-host (api.line.me) Kiota client
/// and exposes the one operation the spec defines through a convenience method.
///
/// For lower-level operations, the generated builders are directly accessible via <see cref="Api"/>.
///
/// Usage:
///   var shop = ShopClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
///   await shop.SendMissionStickerAsync(new MissionStickerRequest { /* ... */ });
/// </summary>
public sealed class ShopClient
{
    /// <summary>The generated client (exposed for low-level operations).</summary>
    public ShopApiClient Api { get; }

    /// <param name="authProvider">Authentication provider (static or refreshing, either works).</param>
    /// <param name="httpClient">
    /// The <see cref="HttpClient"/> shared by the adapter. Supplied by <c>IHttpClientFactory</c>
    /// via DI. When null, the adapter creates its own default <see cref="HttpClient"/>.
    /// </param>
    public ShopClient(IAuthenticationProvider authProvider, HttpClient? httpClient = null)
    {
        if (authProvider is null) throw new ArgumentNullException(nameof(authProvider));

        var adapter = new HttpClientRequestAdapter(authProvider, httpClient: httpClient);
        Api = new ShopApiClient(adapter);
    }

    /// <summary>Helper to quickly construct from a long-lived channel access token.</summary>
    public static ShopClient CreateWithStaticToken(string channelAccessToken)
    {
        var provider = new StaticChannelAccessTokenProvider(channelAccessToken, LineHosts.Api);
        var auth = new BaseBearerTokenAuthenticationProvider(provider);
        return new ShopClient(auth);
    }

    /// <summary>Sends a mission sticker to a user (POST /shop/v3/mission).</summary>
    public async Task SendMissionStickerAsync(
        MissionStickerRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        // The response body is empty. Dispose the Stream the generated code returns.
        using var _ = await Api.Shop.V3.Mission
            .PostAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
