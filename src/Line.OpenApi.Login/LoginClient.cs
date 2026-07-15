using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Line.OpenApi.Core.Authentication;
using Line.OpenApi.Login.Internal;
using Line.OpenApi.Login.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Bundle;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace Line.OpenApi.Login;

/// <summary>
/// Facade for LINE Login v2.1 and its OpenID Connect features. Because LINE publishes no
/// OpenAPI spec for LINE Login, this client is hand-written on top of the Kiota runtime.
///
/// <para>
/// It covers the user-facing OAuth 2.0 authorization-code flow (with optional PKCE):
/// build the authorization URL, exchange the code for tokens, refresh, revoke, and verify an
/// access token; verify an ID token by delegating to LINE (<c>POST /oauth2/v2.1/verify</c>);
/// and read the user's profile / OpenID userinfo / friendship status with a user access token,
/// plus deauthorize the app.
/// </para>
///
/// <para>
/// <b>Credential note.</b> LINE Login <b>user access tokens</b> are a different credential
/// system from Messaging <b>channel access tokens</b>. Token issuance uses the channel ID and
/// channel secret in the request body (no Authorization header). The profile / userinfo /
/// friendship calls take a user access token per call. <c>Deauthorize</c> is a cross-system
/// operation: its Authorization header must be a <b>Messaging channel access token</b> while the
/// body carries the user access token; this client takes that channel token as a plain string
/// argument, so it never depends on Line.OpenApi.ChannelAccessToken.
/// </para>
///
/// <para>All REST calls target api.line.me; the authorization page is on access.line.me
/// (browser redirect only, built by <see cref="BuildAuthorizationUrl"/>).</para>
/// </summary>
public sealed class LoginClient
{
    private static readonly string BaseUrl = $"https://{LineHosts.Api}";

    // Maps non-2xx responses to LoginErrorResponse so the OAuth error/error_description are
    // surfaced (as a thrown ApiException) instead of being collapsed to a bare status code.
    private static readonly Dictionary<string, ParsableFactory<IParsable>> ErrorMapping = new()
    {
        { "4XX", LoginErrorResponse.CreateFromDiscriminatorValue },
        { "5XX", LoginErrorResponse.CreateFromDiscriminatorValue },
    };

    private readonly string _channelId;
    private readonly string _channelSecret;
    private readonly HttpClient _httpClient;
    private readonly string[] _allowedHosts;
    private readonly IRequestAdapter _anonymousAdapter;

    /// <param name="channelId">LINE Login channel ID (used as <c>client_id</c>).</param>
    /// <param name="channelSecret">LINE Login channel secret (used as <c>client_secret</c>).</param>
    /// <param name="httpClient">
    /// The <see cref="HttpClient"/> shared by the request adapters. Supplied by
    /// <c>IHttpClientFactory</c> via DI (shared handler pool, Kiota default middleware applied,
    /// including the CVE-fixed RedirectHandler). When null, a default client with Kiota's
    /// default middleware is created and reused for the lifetime of this instance.
    /// </param>
    /// <param name="allowedHosts">
    /// Hosts a user/channel access token may be attached to. Defaults to api.line.me only
    /// (LINE Login has no data-plane host).
    /// </param>
    public LoginClient(
        string channelId,
        string channelSecret,
        HttpClient? httpClient = null,
        string[]? allowedHosts = null)
    {
        if (string.IsNullOrWhiteSpace(channelId))
            throw new ArgumentException("channelId is required", nameof(channelId));
        if (string.IsNullOrWhiteSpace(channelSecret))
            throw new ArgumentException("channelSecret is required", nameof(channelSecret));

        _channelId = channelId;
        _channelSecret = channelSecret;
        _allowedHosts = allowedHosts is { Length: > 0 } ? allowedHosts : new[] { LineHosts.Api };
        // Reuse one HttpClient across adapters. In the quick path (no DI) create one with Kiota's
        // default middleware so the CVE-fixed RedirectHandler is present.
        _httpClient = httpClient ?? KiotaClientFactory.Create();

        // Token/verify endpoints are unauthenticated (client credentials live in the body).
        _anonymousAdapter = NewAdapter(new AnonymousAuthenticationProvider());
    }

    // ---- Authorization URL (build only; not an HTTP call) --------------------------------

    /// <summary>
    /// Builds the LINE Login authorization URL
    /// (<c>https://access.line.me/oauth2/v2.1/authorize</c>) to redirect the browser to.
    /// </summary>
    /// <param name="parameters">Redirect URI, scopes, state, and optional nonce/PKCE settings.</param>
    /// <returns>The absolute authorization URL to redirect the user agent to.</returns>
    public string BuildAuthorizationUrl(AuthorizationUrlParameters parameters)
    {
        if (parameters is null) throw new ArgumentNullException(nameof(parameters));
        if (string.IsNullOrEmpty(parameters.RedirectUri))
            throw new ArgumentException("RedirectUri is required", nameof(parameters));
        if (parameters.Scopes is null || parameters.Scopes.Count == 0)
            throw new ArgumentException("At least one scope is required", nameof(parameters));
        if (string.IsNullOrEmpty(parameters.State))
            throw new ArgumentException("State is required", nameof(parameters));

        var query = new List<KeyValuePair<string, string>>
        {
            new("response_type", "code"),
            new("client_id", _channelId),
            new("redirect_uri", parameters.RedirectUri),
            new("state", parameters.State),
            new("scope", string.Join(" ", parameters.Scopes)),
        };
        if (parameters.Nonce is not null) query.Add(new("nonce", parameters.Nonce));
        if (parameters.CodeChallenge is not null)
        {
            query.Add(new("code_challenge", parameters.CodeChallenge));
            query.Add(new("code_challenge_method", parameters.CodeChallengeMethod));
        }
        if (parameters.Prompt is not null) query.Add(new("prompt", parameters.Prompt));
        if (parameters.BotPrompt is not null) query.Add(new("bot_prompt", parameters.BotPrompt));
        if (parameters.UiLocales is not null) query.Add(new("ui_locales", parameters.UiLocales));
        if (parameters.ResponseMode is not null) query.Add(new("response_mode", parameters.ResponseMode));

        var encoded = string.Join(
            "&",
            query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return $"https://{LineHosts.AccessLine}/oauth2/v2.1/authorize?{encoded}";
    }

    // ---- OAuth token lifecycle (anonymous; client credentials in body) -------------------

    /// <summary>
    /// Exchanges an authorization code for tokens (<c>POST /oauth2/v2.1/token</c>,
    /// <c>grant_type=authorization_code</c>). Supply <paramref name="codeVerifier"/> when the
    /// authorization request used PKCE.
    /// </summary>
    /// <param name="code">Authorization code received on the callback.</param>
    /// <param name="redirectUri">Must match the redirect URI used in the authorization request.</param>
    /// <param name="codeVerifier">PKCE code verifier; pass it only if PKCE was used.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The issued tokens (access/refresh, plus the ID token when <c>openid</c> was requested).</returns>
    public Task<LineLoginTokenResponse?> ExchangeCodeAsync(
        string code,
        string redirectUri,
        string? codeVerifier = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(code)) throw new ArgumentException("code is required", nameof(code));
        if (string.IsNullOrEmpty(redirectUri))
            throw new ArgumentException("redirectUri is required", nameof(redirectUri));

        var body = new FormFields(
            FormFields.Field("grant_type", "authorization_code"),
            FormFields.Field("code", code),
            FormFields.Field("redirect_uri", redirectUri),
            FormFields.Field("client_id", _channelId),
            FormFields.Field("client_secret", _channelSecret),
            FormFields.Field("code_verifier", codeVerifier));

        return SendFormAsync(
            "{+baseurl}/oauth2/v2.1/token", body,
            LineLoginTokenResponse.CreateFromDiscriminatorValue, cancellationToken);
    }

    /// <summary>
    /// Obtains a new access token from a refresh token (<c>POST /oauth2/v2.1/token</c>,
    /// <c>grant_type=refresh_token</c>).
    /// </summary>
    /// <param name="refreshToken">A valid refresh token (up to 90 days old).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A new access token (and echoed refresh token).</returns>
    public Task<LineLoginTokenResponse?> RefreshTokenAsync(
        string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(refreshToken))
            throw new ArgumentException("refreshToken is required", nameof(refreshToken));

        var body = new FormFields(
            FormFields.Field("grant_type", "refresh_token"),
            FormFields.Field("refresh_token", refreshToken),
            FormFields.Field("client_id", _channelId),
            FormFields.Field("client_secret", _channelSecret));

        return SendFormAsync(
            "{+baseurl}/oauth2/v2.1/token", body,
            LineLoginTokenResponse.CreateFromDiscriminatorValue, cancellationToken);
    }

    /// <summary>Revokes an access token (<c>POST /oauth2/v2.1/revoke</c>). Returns on success (HTTP 200).</summary>
    /// <param name="accessToken">The access token to revoke.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task RevokeTokenAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(accessToken))
            throw new ArgumentException("accessToken is required", nameof(accessToken));

        var body = new FormFields(
            FormFields.Field("access_token", accessToken),
            FormFields.Field("client_id", _channelId),
            FormFields.Field("client_secret", _channelSecret));

        var req = new RequestInformation(Method.POST, "{+baseurl}/oauth2/v2.1/revoke", PathParams());
        req.SetContentFromParsable(_anonymousAdapter, "application/x-www-form-urlencoded", body);
        return _anonymousAdapter.SendNoContentAsync(req, ErrorMapping, cancellationToken);
    }

    /// <summary>
    /// Verifies the validity of an access token (<c>GET /oauth2/v2.1/verify</c>) and returns its
    /// scope, channel ID, and remaining lifetime.
    /// </summary>
    /// <param name="accessToken">The access token to verify.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The token's scope, channel ID, and remaining lifetime.</returns>
    public Task<VerifyAccessTokenResponse?> VerifyAccessTokenAsync(
        string accessToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(accessToken))
            throw new ArgumentException("accessToken is required", nameof(accessToken));

        var req = new RequestInformation(
            Method.GET, "{+baseurl}/oauth2/v2.1/verify{?access_token}", PathParams());
        req.QueryParameters.Add("access_token", accessToken);
        req.Headers.TryAdd("Accept", "application/json");
        return _anonymousAdapter.SendAsync(
            req, VerifyAccessTokenResponse.CreateFromDiscriminatorValue,
            ErrorMapping, cancellationToken);
    }

    // ---- OpenID Connect ID-token verification (server-side delegation) -------------------

    /// <summary>
    /// Verifies an ID token by delegating to LINE (<c>POST /oauth2/v2.1/verify</c>). LINE checks
    /// the signature and the standard claims and returns the verified claims. This is the
    /// simplest, always-correct path; local verification (HS256/ES256 + JWKS) is not included in
    /// this release.
    /// </summary>
    /// <param name="idToken">The ID token (JWT) from the token response.</param>
    /// <param name="nonce">Expected nonce from the authorization request, if one was sent.</param>
    /// <param name="expectedUserId">Expected user ID, to assert the token's subject.</param>
    public Task<VerifiedIdToken?> VerifyIdTokenAsync(
        string idToken,
        string? nonce = null,
        string? expectedUserId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(idToken))
            throw new ArgumentException("idToken is required", nameof(idToken));

        var body = new FormFields(
            FormFields.Field("id_token", idToken),
            FormFields.Field("client_id", _channelId),
            FormFields.Field("nonce", nonce),
            FormFields.Field("user_id", expectedUserId));

        return SendFormAsync(
            "{+baseurl}/oauth2/v2.1/verify", body,
            VerifiedIdToken.CreateFromDiscriminatorValue, cancellationToken);
    }

    // ---- User-access-token calls ---------------------------------------------------------

    /// <summary>
    /// Gets the OpenID Connect userinfo (<c>GET /oauth2/v2.1/userinfo</c>). Requires a user
    /// access token with the <c>openid</c> scope.
    /// </summary>
    /// <param name="userAccessToken">A user access token (openid scope).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The OpenID userinfo (sub, and name/picture with the profile scope).</returns>
    public Task<UserInfo?> GetUserInfoAsync(
        string userAccessToken, CancellationToken cancellationToken = default)
        => SendWithUserTokenAsync(
            userAccessToken, "{+baseurl}/oauth2/v2.1/userinfo",
            UserInfo.CreateFromDiscriminatorValue, cancellationToken);

    /// <summary>
    /// Gets the user's profile (<c>GET /v2/profile</c>). Requires a user access token with the
    /// <c>profile</c> scope.
    /// </summary>
    /// <param name="userAccessToken">A user access token (profile scope).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user's profile (userId, displayName, and optional picture/status).</returns>
    public Task<LineUserProfile?> GetProfileAsync(
        string userAccessToken, CancellationToken cancellationToken = default)
        => SendWithUserTokenAsync(
            userAccessToken, "{+baseurl}/v2/profile",
            LineUserProfile.CreateFromDiscriminatorValue, cancellationToken);

    /// <summary>
    /// Gets the friendship status between the user and the Official Account linked to the LINE
    /// Login channel (<c>GET /friendship/v1/status</c>). Requires a user access token with the
    /// <c>profile</c> scope.
    /// </summary>
    /// <param name="userAccessToken">A user access token (profile scope).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The friendship status (<c>FriendFlag</c>).</returns>
    public Task<FriendshipStatus?> GetFriendshipStatusAsync(
        string userAccessToken, CancellationToken cancellationToken = default)
        => SendWithUserTokenAsync(
            userAccessToken, "{+baseurl}/friendship/v1/status",
            FriendshipStatus.CreateFromDiscriminatorValue, cancellationToken);

    /// <summary>
    /// Revokes all permissions the user granted to the app (<c>POST /user/v1/deauthorize</c>).
    /// <b>The Authorization header is a Messaging channel access token</b> (not the user token);
    /// the user access token is sent in the body. Returns on success (HTTP 2xx).
    /// </summary>
    /// <param name="channelAccessToken">A Messaging API channel access token (v2.1 or stateless).</param>
    /// <param name="userAccessToken">The user access token to deauthorize.</param>
    public Task DeauthorizeAsync(
        string channelAccessToken,
        string userAccessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(channelAccessToken))
            throw new ArgumentException("channelAccessToken is required", nameof(channelAccessToken));
        if (string.IsNullOrEmpty(userAccessToken))
            throw new ArgumentException("userAccessToken is required", nameof(userAccessToken));

        var adapter = NewBearerAdapter(channelAccessToken);
        var body = new FormFields(FormFields.Field("userAccessToken", userAccessToken));
        var req = new RequestInformation(Method.POST, "{+baseurl}/user/v1/deauthorize", PathParams());
        req.SetContentFromParsable(adapter, "application/json", body);
        return adapter.SendNoContentAsync(req, ErrorMapping, cancellationToken);
    }

    // ---- Internals -----------------------------------------------------------------------

    private Task<T?> SendFormAsync<T>(
        string urlTemplate,
        FormFields body,
        ParsableFactory<T> factory,
        CancellationToken cancellationToken) where T : IParsable
    {
        var req = new RequestInformation(Method.POST, urlTemplate, PathParams());
        req.Headers.TryAdd("Accept", "application/json");
        req.SetContentFromParsable(_anonymousAdapter, "application/x-www-form-urlencoded", body);
        return _anonymousAdapter.SendAsync(req, factory, ErrorMapping, cancellationToken);
    }

    private Task<T?> SendWithUserTokenAsync<T>(
        string userAccessToken,
        string urlTemplate,
        ParsableFactory<T> factory,
        CancellationToken cancellationToken) where T : IParsable
    {
        if (string.IsNullOrEmpty(userAccessToken))
            throw new ArgumentException("userAccessToken is required", nameof(userAccessToken));

        var adapter = NewBearerAdapter(userAccessToken);
        var req = new RequestInformation(Method.GET, urlTemplate, PathParams());
        req.Headers.TryAdd("Accept", "application/json");
        return adapter.SendAsync(req, factory, ErrorMapping, cancellationToken);
    }

    // Fresh path parameters per call: exposing a shared mutable dictionary would let one call
    // rewrite baseurl for another.
    private static Dictionary<string, object> PathParams()
        => new() { { "baseurl", BaseUrl } };

    private IRequestAdapter NewAdapter(IAuthenticationProvider authProvider)
    {
        // Set BaseUrl so the adapter resolves the "{+baseurl}" URL templates to api.line.me.
        // (The adapter would otherwise overwrite the request's baseurl path parameter with its
        // own empty BaseUrl, producing a relative, invalid URI.)
        var adapter = new DefaultRequestAdapter(authProvider, httpClient: _httpClient)
        {
            BaseUrl = BaseUrl,
        };
        return adapter;
    }

    // Per-call adapter that attaches the given Bearer token, host-gated by StaticBearerTokenProvider.
    private IRequestAdapter NewBearerAdapter(string token)
        => NewAdapter(new BaseBearerTokenAuthenticationProvider(
            new StaticBearerTokenProvider(token, _allowedHosts)));
}
