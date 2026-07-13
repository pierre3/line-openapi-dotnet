# Dependency Injection & Hosting

For anything beyond a quick script, register the clients with dependency injection. Compared to
`CreateWithStaticToken`, the DI setup uses a named `IHttpClientFactory` client — so handlers are
pooled and rotated — and applies Kiota's default middleware, which includes the CVE-fixed
`RedirectHandler`.

## Messaging

Static token:

```csharp
using Line.OpenApi.Messaging.DependencyInjection;

services.AddLineMessaging(o => o.ChannelAccessToken = "CHANNEL_ACCESS_TOKEN");
// resolve: sp.GetRequiredService<MessagingClient>()
```

With a custom authentication provider (for example a refreshing token provider from
`Line.OpenApi.ChannelAccessToken`):

```csharp
services.AddLineMessaging(sp =>
{
    // return an IAuthenticationProvider, e.g. built from RefreshingChannelAccessTokenProvider
    return BuildAuthProvider(sp);
});
```

This auth-provider overload is the injection path that keeps `Line.OpenApi.Messaging` from taking a
dependency on `Line.OpenApi.ChannelAccessToken`.

## LIFF

```csharp
using Line.OpenApi.Liff.DependencyInjection;

services.AddLineLiff(o => o.ChannelAccessToken = "CHANNEL_ACCESS_TOKEN");
// resolve: sp.GetRequiredService<LiffClient>()
```

`AddLineLiff` also has the auth-provider overload, mirroring `AddLineMessaging`.

## Webhook

```csharp
using Line.OpenApi.Messaging.Webhook.DependencyInjection;

services.AddLineWebhook(o => o.ChannelSecret = "CHANNEL_SECRET");
// resolve: sp.GetRequiredService<WebhookRequestParser>()
```

Because webhook receiving does no outbound HTTP, `AddLineWebhook` does not use
`IHttpClientFactory`.

## Idempotency and multiple registrations

- `AddLineMessaging` / `AddLineLiff` are **idempotent**: calling them repeatedly does not add
  Kiota's default handlers to the named client more than once (duplicates would multiply
  retries/redirects). The client itself is registered with `TryAdd`, so the **first**
  auth-provider setup wins.
- `AddLineWebhook` registers the parser with `TryAdd` (first-wins), while the options are applied
  cumulatively, so the effective `ChannelSecret` is the **last** one configured (last-wins). It
  also calls `ValidateOnStart`, so a missing secret fails at startup rather than silently letting
  unverified requests through.

## Allowed hosts

`LineMessagingOptions.AllowedHosts` / `LineLiffOptions.AllowedHosts` control which hosts the
token may be attached to. Leave them unset to use the defaults (`api.line.me` [+ `api-data.line.me`
for Messaging]). See [Security](security.md).
