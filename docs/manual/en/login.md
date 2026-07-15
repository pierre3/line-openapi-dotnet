# LINE Login & OpenID Connect

@Line.OpenApi.Login.LoginClient is the facade for LINE Login v2.1 and its OpenID Connect
features. LINE publishes no OpenAPI spec for LINE Login, so this client is hand-written on top
of the Kiota runtime.

> **Credential note.** LINE Login authenticates users with a **user access token**, which is a
> different credential system from the Messaging **channel access token**. Token issuance uses
> the LINE Login channel's ID (`client_id`) and secret (`client_secret`) in the request body.
> All REST calls target `api.line.me`; the authorization page is on `access.line.me` (a browser
> redirect, not a REST endpoint).

```csharp
using Line.OpenApi.Login;

var login = new LoginClient("LOGIN_CHANNEL_ID", "LOGIN_CHANNEL_SECRET");
```

## 1. Build the authorization URL

`BuildAuthorizationUrl` only composes a URL — it makes no HTTP call. Generate a CSRF `state`
and (recommended) a PKCE challenge, store them in the session, then redirect the browser.

```csharp
PkceChallenge pkce = LineLoginSecurity.CreatePkceChallenge();
string state       = LineLoginSecurity.GenerateState();

string url = login.BuildAuthorizationUrl(new AuthorizationUrlParameters
{
    RedirectUri   = "https://app.example.com/callback",
    Scopes        = new[] { "openid", "profile" },
    State         = state,
    Nonce         = "server-generated-nonce",   // echoed into the ID token
    CodeChallenge = pkce.CodeChallenge,          // CodeChallengeMethod defaults to S256
});
// Redirect the user agent to `url`; keep `state` and `pkce.CodeVerifier` in the session.
```

## 2. Exchange the authorization code for tokens

On the callback, verify the returned `state` against the stored value, then exchange the code.
Pass the stored `CodeVerifier` when PKCE was used.

```csharp
LineLoginTokenResponse? token =
    await login.ExchangeCodeAsync("<code>", "https://app.example.com/callback", pkce.CodeVerifier);

string accessToken  = token!.AccessToken!;   // valid 30 days
string refreshToken = token.RefreshToken!;   // valid up to 90 days
string? idToken     = token.IdToken;         // present only when the openid scope was granted
```

## 3. Verify the ID token (OpenID Connect)

`VerifyIdTokenAsync` delegates signature and claim verification to LINE
(`POST /oauth2/v2.1/verify`) and returns the verified claims. This is the simplest,
always-correct path.

```csharp
VerifiedIdToken? claims = await login.VerifyIdTokenAsync(
    idToken!, nonce: "server-generated-nonce", expectedUserId: null);

string userId = claims!.Sub!;   // subject
string? name  = claims.Name;    // present with the profile scope
```

> Local verification (HS256 for the web flow, ES256 + JWKS for native / LIFF flows) is not
> included in this release. Use the server-side delegation above.

## 4. Refresh, revoke, and verify access tokens

```csharp
LineLoginTokenResponse? refreshed = await login.RefreshTokenAsync(refreshToken);
VerifyAccessTokenResponse? info    = await login.VerifyAccessTokenAsync(accessToken); // scope / expiry
await login.RevokeTokenAsync(accessToken);
```

## 5. Read the profile, userinfo, and friendship status

These take the **user access token** per call (host-gated so the token is never sent outside
`api.line.me`).

```csharp
LineUserProfile? profile = await login.GetProfileAsync(accessToken);          // requires profile scope
UserInfo?        userinfo = await login.GetUserInfoAsync(accessToken);        // requires openid scope
FriendshipStatus? friend  = await login.GetFriendshipStatusAsync(accessToken); // friend.FriendFlag
```

## 6. Deauthorize

Revokes all permissions the user granted to the app. Note the cross-system requirement: the
**Authorization header is a Messaging channel access token**, while the user access token is
sent in the body. The channel token is passed as a plain string, so `Line.OpenApi.Login` takes
no dependency on `Line.OpenApi.ChannelAccessToken`.

```csharp
await login.DeauthorizeAsync("MESSAGING_CHANNEL_ACCESS_TOKEN", userAccessToken);
```

## Dependency injection

```csharp
using Line.OpenApi.Login.DependencyInjection;

services.AddLineLogin(o =>
{
    o.ChannelId     = "LOGIN_CHANNEL_ID";
    o.ChannelSecret = "LOGIN_CHANNEL_SECRET";
});
// resolve: sp.GetRequiredService<LoginClient>()
```

See [Dependency Injection & Hosting](di-and-hosting.md) for how the shared `HttpClient` and the
Kiota default middleware (including the CVE-fixed redirect handler) are wired.
