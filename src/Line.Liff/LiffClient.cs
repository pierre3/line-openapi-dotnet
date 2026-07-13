using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Line.Core.Authentication;
using Line.Liff.Generated;
using Line.Liff.Generated.Models;

namespace Line.Liff;

/// <summary>
/// Facade for the LIFF management API. It wraps a single-host (api.line.me) Kiota client and
/// provides the priority use case "list / add / update / delete LIFF apps" through thin
/// convenience methods.
///
/// Unlike Messaging, there is no data-plane host, so no host override (BaseUrl) is needed and
/// the generated default of api.line.me is used as-is. For lower-level operations, the
/// generated builders are directly accessible via <see cref="Api"/>.
///
/// Design note (the intent behind the asymmetry with Messaging): LIFF is a small closed
/// surface of 2 paths and 4 operations, so convenience methods can cover it completely.
/// Messaging has many endpoints where complete coverage is impractical, so it exposes the
/// generated builders directly. This difference is not an inconsistency but a decision scaled
/// to the size of the surface.
///
/// Usage:
///   var liff = LiffClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
///   var apps = await liff.GetAppsAsync();
///   var added = await liff.AddAppAsync(new AddLiffAppRequest { /* ... */ });
///   await liff.UpdateAppAsync(added!.LiffId!, new UpdateLiffAppRequest { /* ... */ });
///   await liff.DeleteAppAsync(added.LiffId!);
/// </summary>
public sealed class LiffClient
{
    /// <summary>The generated client (exposed for low-level operations).</summary>
    public LiffApiClient Api { get; }

    /// <param name="authProvider">Authentication provider (static or refreshing, either works).</param>
    /// <param name="httpClient">
    /// The <see cref="HttpClient"/> shared by the adapter. Supplied by <c>IHttpClientFactory</c>
    /// via DI (shared handler pool, Kiota default middleware applied). When null, the adapter
    /// creates its own default <see cref="HttpClient"/> (for quick use).
    /// </param>
    public LiffClient(IAuthenticationProvider authProvider, HttpClient? httpClient = null)
    {
        if (authProvider is null) throw new ArgumentNullException(nameof(authProvider));

        var adapter = new HttpClientRequestAdapter(authProvider, httpClient: httpClient);
        Api = new LiffApiClient(adapter);
    }

    /// <summary>Helper to quickly construct from a long-lived channel access token.</summary>
    public static LiffClient CreateWithStaticToken(string channelAccessToken)
    {
        var provider = new StaticChannelAccessTokenProvider(channelAccessToken, LineHosts.Api);
        var auth = new BaseBearerTokenAuthenticationProvider(provider);
        return new LiffClient(auth);
    }

    /// <summary>Gets all LIFF apps registered on the channel (GET /liff/v1/apps).</summary>
    public Task<GetAllLiffAppsResponse?> GetAppsAsync(CancellationToken cancellationToken = default)
        => Api.Liff.V1.Apps.GetAsync(cancellationToken: cancellationToken);

    /// <summary>Adds a LIFF app to the channel (POST /liff/v1/apps). Returns the response containing the issued LIFF ID.</summary>
    public Task<AddLiffAppResponse?> AddAppAsync(
        AddLiffAppRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        return Api.Liff.V1.Apps.PostAsync(request, cancellationToken: cancellationToken);
    }

    /// <summary>Updates the settings of an existing LIFF app (PUT /liff/v1/apps/{liffId}).</summary>
    public async Task UpdateAppAsync(
        string liffId, UpdateLiffAppRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(liffId)) throw new ArgumentException("liffId is required", nameof(liffId));
        if (request is null) throw new ArgumentNullException(nameof(request));

        // The response body is empty. Dispose the Stream the generated code returns.
        using var _ = await Api.Liff.V1.Apps[liffId]
            .PutAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes a LIFF app from the channel (DELETE /liff/v1/apps/{liffId}).</summary>
    public async Task DeleteAppAsync(string liffId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(liffId)) throw new ArgumentException("liffId is required", nameof(liffId));

        using var _ = await Api.Liff.V1.Apps[liffId]
            .DeleteAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
