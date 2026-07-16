using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Line.OpenApi.Core.Authentication;
using Line.OpenApi.MiniApp.Internal;
using Line.OpenApi.MiniApp.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Bundle;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Line.OpenApi.MiniApp;

/// <summary>
/// Facade for the LINE MINI App server REST surface. Because LINE publishes no OpenAPI spec for
/// LINE MINI App, this client is hand-written on top of the Kiota runtime.
///
/// <para>
/// It covers two independent feature areas, both on <c>api.line.me</c>:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Service messages</b> (<see cref="IssueNotificationTokenAsync"/>,
/// <see cref="SendServiceMessageAsync"/>): notify a user in response to an action they took in
/// the MINI App. Requires a <b>channel access token</b> (stateless/short-lived only; long-lived
/// v2.1 tokens are not accepted by these endpoints).
/// </description></item>
/// <item><description>
/// <b>In-app purchase (IAP)</b> (<see cref="ReserveProductAsync"/>,
/// <see cref="GetWebhookEventsAsync"/>): reserve a purchase (requires a <b>user access token</b>)
/// and read the platform's purchase/refund webhook history (requires a channel access token).
/// </description></item>
/// </list>
///
/// <para>
/// <b>Credential note.</b> Like <c>LoginClient</c>, tokens are taken as plain string arguments
/// per call rather than stored, so this client never depends on
/// <c>Line.OpenApi.ChannelAccessToken</c> or <c>Line.OpenApi.Login</c>.
/// </para>
/// </summary>
public sealed class MiniAppClient
{
    private static readonly string BaseUrl = $"https://{LineHosts.Api}";

    private static readonly Dictionary<string, ParsableFactory<IParsable>> NotifierErrorMapping = new()
    {
        { "4XX", NotifierErrorResponse.CreateFromDiscriminatorValue },
        { "5XX", NotifierErrorResponse.CreateFromDiscriminatorValue },
    };

    private static readonly Dictionary<string, ParsableFactory<IParsable>> IapErrorMapping = new()
    {
        { "4XX", IapErrorResponse.CreateFromDiscriminatorValue },
        { "5XX", IapErrorResponse.CreateFromDiscriminatorValue },
    };

    private readonly HttpClient _httpClient;
    private readonly string[] _allowedHosts;

    /// <param name="httpClient">
    /// The <see cref="HttpClient"/> shared by the request adapters. Supplied by
    /// <c>IHttpClientFactory</c> via DI (shared handler pool, Kiota default middleware applied,
    /// including the CVE-fixed RedirectHandler). When null, a default client with Kiota's
    /// default middleware is created and reused for the lifetime of this instance.
    /// </param>
    /// <param name="allowedHosts">
    /// Hosts a channel/user access token may be attached to. Defaults to api.line.me only
    /// (LINE MINI App has no data-plane host).
    /// </param>
    public MiniAppClient(HttpClient? httpClient = null, string[]? allowedHosts = null)
    {
        _allowedHosts = allowedHosts is { Length: > 0 } ? allowedHosts : new[] { LineHosts.Api };
        _httpClient = httpClient ?? KiotaClientFactory.Create();
    }

    // ---- Service messages -----------------------------------------------------------------

    /// <summary>
    /// Issues a service notification token (<c>POST /message/v3/notifier/token</c>) for a user
    /// action. Valid for 1 year; can be used to send up to 5 service messages.
    /// </summary>
    /// <param name="channelAccessToken">A stateless/short-lived channel access token.</param>
    /// <param name="liffAccessToken">The user access token issued by <c>liff.getAccessToken()</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The notification token, its expiry, remaining send count, and session ID.</returns>
    public Task<NotifierToken?> IssueNotificationTokenAsync(
        string channelAccessToken, string liffAccessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(channelAccessToken))
            throw new ArgumentException("channelAccessToken is required", nameof(channelAccessToken));
        if (string.IsNullOrEmpty(liffAccessToken))
            throw new ArgumentException("liffAccessToken is required", nameof(liffAccessToken));

        var body = new FlatFields(FlatFields.Field("liffAccessToken", liffAccessToken));
        return PostNotifierAsync("{+baseurl}/message/v3/notifier/token", channelAccessToken, body, cancellationToken);
    }

    /// <summary>
    /// Sends a service message (<c>POST /message/v3/notifier/send?target=service</c>). The
    /// response carries a renewed notification token; save it for the next send.
    /// </summary>
    /// <param name="channelAccessToken">A stateless/short-lived channel access token.</param>
    /// <param name="notificationToken">The token from the last issue/send call.</param>
    /// <param name="templateName">Reviewed template name, formatted as <c>{name}_{BCP-47 language}</c>.</param>
    /// <param name="parameters">Template variable/value pairs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The renewed notification token, its expiry, remaining send count, and session ID.</returns>
    public Task<NotifierToken?> SendServiceMessageAsync(
        string channelAccessToken,
        string notificationToken,
        string templateName,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(channelAccessToken))
            throw new ArgumentException("channelAccessToken is required", nameof(channelAccessToken));
        if (string.IsNullOrEmpty(notificationToken))
            throw new ArgumentException("notificationToken is required", nameof(notificationToken));
        if (string.IsNullOrEmpty(templateName))
            throw new ArgumentException("templateName is required", nameof(templateName));
        if (parameters is null) throw new ArgumentNullException(nameof(parameters));

        var body = new SendServiceMessageRequestBody(templateName, parameters, notificationToken);
        var adapter = NewBearerAdapter(channelAccessToken);
        var req = new RequestInformation(
            Method.POST, "{+baseurl}/message/v3/notifier/send{?target}", PathParams());
        req.QueryParameters.Add("target", "service");
        req.Headers.TryAdd("Accept", "application/json");
        req.SetContentFromParsable(adapter, "application/json", body);
        return adapter.SendAsync(req, NotifierToken.CreateFromDiscriminatorValue, NotifierErrorMapping, cancellationToken);
    }

    // ---- In-app purchase (IAP) --------------------------------------------------------------

    /// <summary>
    /// Reserves an in-app purchase (<c>POST /iap/v1/product/reserve</c>). Hand the returned
    /// order ID to the in-app purchase SDK to complete the purchase.
    /// </summary>
    /// <param name="userAccessToken">The purchasing user's user access token.</param>
    /// <param name="clientIp">The user's client IPv4 or IPv6 address.</param>
    /// <param name="clientOs"><c>ios</c> or <c>android</c>.</param>
    /// <param name="productId">Identifier of the product being purchased.</param>
    /// <param name="shopProductName">Display name shown in the purchase UI (max 20 UTF-16 chars, no emoji/symbols).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The reserved order.</returns>
    public Task<IapReserveResult?> ReserveProductAsync(
        string userAccessToken,
        string clientIp,
        string clientOs,
        string productId,
        string shopProductName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userAccessToken))
            throw new ArgumentException("userAccessToken is required", nameof(userAccessToken));
        if (string.IsNullOrEmpty(clientIp))
            throw new ArgumentException("clientIp is required", nameof(clientIp));
        if (string.IsNullOrEmpty(clientOs))
            throw new ArgumentException("clientOs is required", nameof(clientOs));
        if (string.IsNullOrEmpty(productId))
            throw new ArgumentException("productId is required", nameof(productId));
        if (string.IsNullOrEmpty(shopProductName))
            throw new ArgumentException("shopProductName is required", nameof(shopProductName));

        var body = new FlatFields(
            FlatFields.Field("clientIp", clientIp),
            FlatFields.Field("clientOs", clientOs),
            FlatFields.Field("productId", productId),
            FlatFields.Field("shopProductName", shopProductName));

        var adapter = NewBearerAdapter(userAccessToken);
        var req = new RequestInformation(Method.POST, "{+baseurl}/iap/v1/product/reserve", PathParams());
        req.Headers.TryAdd("Accept", "application/json");
        req.SetContentFromParsable(adapter, "application/json", body);
        return adapter.SendAsync(req, IapReserveResult.CreateFromDiscriminatorValue, IapErrorMapping, cancellationToken);
    }

    /// <summary>
    /// Reads a page of IAP webhook event history (<c>GET /iap/v1/webhook/events</c>), covering
    /// the past 7 days.
    /// </summary>
    /// <param name="channelAccessToken">A channel access token.</param>
    /// <param name="startEpochSeconds">Start of the time range (UNIX time, seconds).</param>
    /// <param name="endEpochSeconds">End of the time range (UNIX time, seconds).</param>
    /// <param name="pageSize">Events per page (1-100).</param>
    /// <param name="cursor">Pagination cursor from a previous page, if any.</param>
    /// <param name="status">Filter by <c>SUCCESS</c> or <c>FAILED</c>; omit for all.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page of events and the cursor for the next page, if any.</returns>
    public Task<MiniAppWebhookEventPage?> GetWebhookEventsAsync(
        string channelAccessToken,
        long startEpochSeconds,
        long endEpochSeconds,
        int pageSize,
        string? cursor = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(channelAccessToken))
            throw new ArgumentException("channelAccessToken is required", nameof(channelAccessToken));
        if (pageSize is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "pageSize must be between 1 and 100");

        var adapter = NewBearerAdapter(channelAccessToken);
        var req = new RequestInformation(
            Method.GET,
            "{+baseurl}/iap/v1/webhook/events{?startEpochSeconds,endEpochSeconds,pageSize,cursor,status}",
            PathParams());
        req.QueryParameters.Add("startEpochSeconds", startEpochSeconds);
        req.QueryParameters.Add("endEpochSeconds", endEpochSeconds);
        req.QueryParameters.Add("pageSize", pageSize);
        if (cursor is not null) req.QueryParameters.Add("cursor", cursor);
        if (status is not null) req.QueryParameters.Add("status", status);
        req.Headers.TryAdd("Accept", "application/json");
        return adapter.SendAsync(
            req, MiniAppWebhookEventPage.CreateFromDiscriminatorValue, IapErrorMapping, cancellationToken);
    }

    // ---- Internals -----------------------------------------------------------------------

    private Task<NotifierToken?> PostNotifierAsync(
        string urlTemplate, string channelAccessToken, IParsable body, CancellationToken cancellationToken)
    {
        var adapter = NewBearerAdapter(channelAccessToken);
        var req = new RequestInformation(Method.POST, urlTemplate, PathParams());
        req.Headers.TryAdd("Accept", "application/json");
        req.SetContentFromParsable(adapter, "application/json", body);
        return adapter.SendAsync(req, NotifierToken.CreateFromDiscriminatorValue, NotifierErrorMapping, cancellationToken);
    }

    // Fresh path parameters per call: exposing a shared mutable dictionary would let one call
    // rewrite baseurl for another.
    private static Dictionary<string, object> PathParams()
        => new() { { "baseurl", BaseUrl } };

    // Per-call adapter that attaches the given Bearer token, host-gated by StaticBearerTokenProvider.
    private IRequestAdapter NewBearerAdapter(string token)
    {
        var authProvider = new BaseBearerTokenAuthenticationProvider(
            new StaticBearerTokenProvider(token, _allowedHosts));
        // Set BaseUrl so the adapter resolves the "{+baseurl}" URL template to api.line.me.
        // (The adapter would otherwise overwrite the request's baseurl path parameter with its
        // own empty BaseUrl, producing a relative, invalid URI.)
        return new DefaultRequestAdapter(authProvider, httpClient: _httpClient) { BaseUrl = BaseUrl };
    }
}
