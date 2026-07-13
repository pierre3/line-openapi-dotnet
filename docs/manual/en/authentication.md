# Authentication

Calls to the Messaging and LIFF APIs are authorized with a **channel access token** carried as
a Bearer token. This library models token acquisition behind Kiota's
`IAccessTokenProvider`, so you can choose between a static token and a runtime-refreshing one
without changing call sites.

## Token strategies at a glance

| Strategy | Type | When to use |
|---|---|---|
| Static (long-lived) | @Line.Core.Authentication.StaticChannelAccessTokenProvider | You already hold a long-lived token. |
| Short-lived (v2.1 JWT) | @Line.ChannelAccessToken.JwtAssertionTokenSource | Issue a token via JWT assertion (`/oauth2/v2.1/token`). |
| Stateless (v3) | @Line.ChannelAccessToken.StatelessJwtAssertionTokenSource | Issue a 15-minute stateless token (`/oauth2/v3/token`). |
| Auto-refresh wrapper | @Line.ChannelAccessToken.RefreshingChannelAccessTokenProvider | Cache a short-lived/stateless token and re-issue near expiry. |

## Static token

The simplest case: hold a token and return it. This provider lives in `Line.Core` and only
attaches the token to allowed hosts (see [Security](security.md)).

```csharp
using Line.Core.Authentication;
using Microsoft.Kiota.Abstractions.Authentication;

var provider = new StaticChannelAccessTokenProvider("CHANNEL_ACCESS_TOKEN");
var auth = new BaseBearerTokenAuthenticationProvider(provider);
```

Both `MessagingClient` and `LiffClient` expose a `CreateWithStaticToken(...)` shortcut that
wires this up for you.

## Short-lived tokens (JWT assertion)

Use @Line.ChannelAccessToken.JwtAssertionTokenSource to issue a short-lived token from a signed
JWT assertion. Signing with your channel's private key is application-specific, so you supply
the assertion through a factory — **this library never handles signing keys**.

```csharp
using Line.ChannelAccessToken;
using Line.ChannelAccessToken.Generated;

var tokenClient = new ChannelAccessTokenClient(requestAdapter); // Kiota-generated client
var source = new JwtAssertionTokenSource(
    tokenClient,
    async ct => await BuildSignedJwtAssertionAsync(ct)); // your signing logic

IssuedToken token = await source.IssueAsync();
```

## Stateless tokens (v3)

@Line.ChannelAccessToken.StatelessJwtAssertionTokenSource issues a **stateless** token from
`/oauth2/v3/token`. Stateless tokens have no limit on the number of concurrently active tokens,
but they live for only 15 minutes and cannot be revoked before expiry — so pair them with the
refreshing provider and issue on demand.

> **Why a dedicated helper?** The `/oauth2/v3/token` body is a discriminator-less `oneOf`. The
> generated code represents it as a composed wrapper that serializes its inner model as a
> *nested object*, which the form-urlencoded serializer cannot express (it fails with
> "Form serialization does not support nested objects."). This helper sidesteps the wrapper by
> sending a flat request model, so you get the same issuance seam as the v2.1 source.

## Auto-refreshing provider

@Line.ChannelAccessToken.RefreshingChannelAccessTokenProvider wraps any
`IChannelAccessTokenSource`, caches the issued token, and re-issues it once the refresh margin
before expiry is reached. It prevents duplicate issuance under concurrent refresh, and it is
`IDisposable`.

```csharp
using Line.ChannelAccessToken;

using var provider = new RefreshingChannelAccessTokenProvider(
    source,                              // e.g. JwtAssertionTokenSource / StatelessJwtAssertionTokenSource
    refreshMargin: TimeSpan.FromMinutes(5));

var auth = new BaseBearerTokenAuthenticationProvider(provider);
var messaging = new MessagingClient(auth);
```

To use a refreshing provider with DI, inject it through the auth-provider overload of
`AddLineMessaging` / `AddLineLiff` — see [Dependency Injection & Hosting](di-and-hosting.md).
This keeps `Line.Messaging` / `Line.Liff` free of a dependency on `Line.ChannelAccessToken`.
