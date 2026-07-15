using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Line.OpenApi.Login;
using Line.OpenApi.Login.DependencyInjection;
using Line.OpenApi.Login.Models;

// LINE OpenApi .NET — LINE Login + OpenID Connect sample (minimal API).
//
// Demonstrates the full browser authorization-code flow with PKCE, end-to-end against the real
// LINE API (real code exchange, LINE-signed ID-token verification, real deauthorize):
//   GET /login    -> build the authorization URL (state + PKCE stored in the session) and redirect
//   GET /callback -> verify state, exchange the code, verify the ID token, then show the profile
//                    and friendship status
//   GET /logout   -> revoke the access token (or deauthorize when a Messaging channel token is set)
//
// Configuration (environment variables; the app always starts when they are UNSET — it then shows
// a "disabled" page. Unlike the offline console sample, LINE Login cannot be exercised without
// real credentials because it is a live browser round-trip):
//   LINE_LOGIN_CHANNEL_ID       LINE Login channel ID       -> enables the flow
//   LINE_LOGIN_CHANNEL_SECRET   LINE Login channel secret   -> enables the flow
//   LINE_LOGIN_REDIRECT_URI     callback URL registered in the LINE console
//                               (default http://localhost:5000/callback)
//   LINE_CHANNEL_ACCESS_TOKEN   Messaging channel access token (optional) -> uses deauthorize
//                               instead of revoke on /logout
//
// The redirect URI must be registered in the LINE Developers Console for the Login channel.
// localhost callbacks are allowed for LINE Login, so this runs locally without a tunnel.

var builder = WebApplication.CreateBuilder(args);

var channelId = Environment.GetEnvironmentVariable("LINE_LOGIN_CHANNEL_ID");
var channelSecret = Environment.GetEnvironmentVariable("LINE_LOGIN_CHANNEL_SECRET");
var redirectUri = Environment.GetEnvironmentVariable("LINE_LOGIN_REDIRECT_URI")
                  ?? "http://localhost:5000/callback";
var messagingChannelToken = Environment.GetEnvironmentVariable("LINE_CHANNEL_ACCESS_TOKEN");

// Validate the redirect URI up front with a clear message (a malformed value would otherwise
// crash startup with a raw UriFormatException).
if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var redirectUriObj))
    throw new InvalidOperationException(
        $"LINE_LOGIN_REDIRECT_URI must be an absolute URL (e.g. http://localhost:5000/callback); got '{redirectUri}'.");

// For a localhost callback (the default), bind Kestrel to that origin so the registered redirect
// URI resolves back here. For a non-localhost redirect URI (e.g. behind a tunnel), leave the
// listen address to ASPNETCORE_URLS and forward the public URL to this app yourself.
if (redirectUriObj.IsLoopback)
    builder.WebHost.UseUrls(redirectUriObj.GetLeftPart(UriPartial.Authority));

// Session holds the per-request state / PKCE verifier / nonce between /login and /callback.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
    // Lax lets the cookie ride the top-level GET callback redirect. In production also set
    // o.Cookie.SecurePolicy = CookieSecurePolicy.Always (requires HTTPS).
    o.Cookie.SameSite = SameSiteMode.Lax;
});

// Register LoginClient via DI (AddLineLogin) only when credentials are present, so the app is
// startable without them. AddLineLogin wires the recommended IHttpClientFactory integration
// (shared handler pool + Kiota default middleware, including the CVE-fixed RedirectHandler).
var configured = !string.IsNullOrWhiteSpace(channelId) && !string.IsNullOrWhiteSpace(channelSecret);
if (configured)
{
    builder.Services.AddLineLogin(o =>
    {
        o.ChannelId = channelId!;
        o.ChannelSecret = channelSecret!;
    });
}

var app = builder.Build();
app.UseSession();

app.MapGet("/", () => Results.Content($$"""
    <html><body style="font-family:sans-serif;max-width:640px;margin:2rem auto">
    <h1>Line.OpenApi.Samples.Login</h1>
    <p>Login: <b>{{(configured ? "enabled" : "disabled — set LINE_LOGIN_CHANNEL_ID / LINE_LOGIN_CHANNEL_SECRET")}}</b></p>
    <p>Redirect URI: <code>{{WebUtility.HtmlEncode(redirectUri)}}</code> (must be registered in the LINE console)</p>
    <p>{{(configured ? "<a href=\"/login\">Sign in with LINE</a>" : "")}}</p>
    </body></html>
    """, "text/html"));

// Step 1: start the flow.
app.MapGet("/login", (HttpContext ctx) =>
{
    var login = ctx.RequestServices.GetService<LoginClient>();
    if (login is null)
        return Results.Problem("LINE Login is not configured (set LINE_LOGIN_CHANNEL_ID / LINE_LOGIN_CHANNEL_SECRET).", statusCode: 503);

    var pkce = LineLoginSecurity.CreatePkceChallenge();
    var state = LineLoginSecurity.GenerateState();
    var nonce = LineLoginSecurity.GenerateState(16);

    // Persist the security parameters to verify them on the callback.
    ctx.Session.SetString("state", state);
    ctx.Session.SetString("code_verifier", pkce.CodeVerifier);
    ctx.Session.SetString("nonce", nonce);

    var url = login.BuildAuthorizationUrl(new AuthorizationUrlParameters
    {
        RedirectUri = redirectUri,
        Scopes = new[] { "openid", "profile" },
        State = state,
        Nonce = nonce,
        CodeChallenge = pkce.CodeChallenge,
    });
    return Results.Redirect(url);
});

// Step 2: handle the callback.
app.MapGet("/callback", async (HttpContext ctx, string? code, string? state, string? error, string? error_description) =>
{
    var login = ctx.RequestServices.GetService<LoginClient>();
    if (login is null)
        return Results.Problem("LINE Login is not configured.", statusCode: 503);

    if (!string.IsNullOrEmpty(error))
        return Html($"<h1>Authorization denied</h1><p>{WebUtility.HtmlEncode(error)}: {WebUtility.HtmlEncode(error_description)}</p>");

    // CSRF protection: the returned state must match the one stored at /login.
    var expectedState = ctx.Session.GetString("state");
    if (string.IsNullOrEmpty(expectedState) || state != expectedState)
        return Results.BadRequest("state mismatch (possible CSRF or expired session).");
    if (string.IsNullOrEmpty(code))
        return Results.BadRequest("missing authorization code.");

    var verifier = ctx.Session.GetString("code_verifier");
    var nonce = ctx.Session.GetString("nonce");

    // Single-use: drop the security parameters now that the code is being redeemed.
    ctx.Session.Remove("state");
    ctx.Session.Remove("code_verifier");
    ctx.Session.Remove("nonce");

    try
    {
        var token = await login.ExchangeCodeAsync(code, redirectUri, verifier);
        if (string.IsNullOrEmpty(token?.AccessToken))
            return Html("<h1>Unexpected response</h1><p>The token response did not contain an access token.</p>");

        VerifiedIdToken? claims = null;
        if (!string.IsNullOrEmpty(token.IdToken))
            claims = await login.VerifyIdTokenAsync(token.IdToken!, nonce: nonce);

        var profile = await login.GetProfileAsync(token.AccessToken!);
        var friendship = await login.GetFriendshipStatusAsync(token.AccessToken!);

        // Keep the access token in the session so /logout can revoke it.
        ctx.Session.SetString("access_token", token.AccessToken!);

        var sb = new StringBuilder();
        sb.Append("<h1>Signed in</h1>");
        sb.Append($"<p><b>userId:</b> {WebUtility.HtmlEncode(profile?.UserId)}</p>");
        sb.Append($"<p><b>displayName:</b> {WebUtility.HtmlEncode(profile?.DisplayName)}</p>");
        if (!string.IsNullOrEmpty(profile?.PictureUrl))
            sb.Append($"<p><img src=\"{WebUtility.HtmlEncode(profile!.PictureUrl)}\" width=\"96\" /></p>");
        sb.Append($"<p><b>friend of the linked OA:</b> {WebUtility.HtmlEncode(friendship?.FriendFlag?.ToString())}</p>");
        if (claims is not null)
        {
            sb.Append("<h2>ID token claims (verified by LINE)</h2>");
            sb.Append($"<p>iss: {WebUtility.HtmlEncode(claims.Iss)}<br/>sub: {WebUtility.HtmlEncode(claims.Sub)}<br/>aud: {WebUtility.HtmlEncode(claims.Aud)}<br/>name: {WebUtility.HtmlEncode(claims.Name)}</p>");
        }
        sb.Append("<p><a href=\"/logout\">Sign out</a></p>");
        return Html(sb.ToString());
    }
    catch (LoginErrorResponse ex)
    {
        // The OAuth error/description are surfaced by the typed LoginErrorResponse.
        return Html($"<h1>Token exchange failed ({ex.ResponseStatusCode})</h1><p>{WebUtility.HtmlEncode(ex.Error)}: {WebUtility.HtmlEncode(ex.ErrorDescription)}</p>");
    }
});

// Optional step 3: sign out. Uses deauthorize (revokes all app permissions) when a Messaging
// channel token is available, otherwise revokes just the access token. They are mutually
// exclusive so a revoked token is never re-sent.
app.MapGet("/logout", async (HttpContext ctx) =>
{
    var login = ctx.RequestServices.GetService<LoginClient>();
    if (login is null)
        return Results.Problem("LINE Login is not configured.", statusCode: 503);

    var accessToken = ctx.Session.GetString("access_token");
    if (string.IsNullOrEmpty(accessToken))
        return Results.Redirect("/");

    ctx.Session.Clear();
    try
    {
        // Deauthorize needs a Messaging channel access token in the Authorization header while the
        // user token goes in the body.
        if (!string.IsNullOrWhiteSpace(messagingChannelToken))
            await login.DeauthorizeAsync(messagingChannelToken!, accessToken);
        else
            await login.RevokeTokenAsync(accessToken);
    }
    catch (LoginErrorResponse ex)
    {
        return Html($"<h1>Sign out failed ({ex.ResponseStatusCode})</h1><p>{WebUtility.HtmlEncode(ex.Error)}</p>");
    }

    return Html("<h1>Signed out</h1><p>Token revoked. <a href=\"/\">Home</a></p>");
});

app.Run();

static IResult Html(string body) => Results.Content(
    $"<html><body style=\"font-family:sans-serif;max-width:640px;margin:2rem auto\">{body}</body></html>",
    "text/html");
