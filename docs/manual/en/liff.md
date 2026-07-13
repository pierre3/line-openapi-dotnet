# LIFF App Management

@Line.Liff.LiffClient is the facade for the LIFF management API. Unlike Messaging, LIFF uses a
single host (`api.line.me`) and a small, closed surface (2 paths, 4 operations), so the client
wraps it completely with convenience methods. For anything lower-level, `LiffClient.Api`
exposes the generated builders directly.

```csharp
using Line.Liff;
using Line.Liff.Generated.Models;

var liff = LiffClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
```

## List apps

```csharp
GetAllLiffAppsResponse? apps = await liff.GetAppsAsync();
```

## Add an app

```csharp
AddLiffAppResponse? added = await liff.AddAppAsync(new AddLiffAppRequest
{
    View = new LiffView { Type = LiffView_type.Full, Url = "https://example.com" },
});
string liffId = added!.LiffId!;
```

## Update an app

```csharp
await liff.UpdateAppAsync(liffId, new UpdateLiffAppRequest { Description = "updated" });
```

## Delete an app

```csharp
await liff.DeleteAppAsync(liffId);
```

## Dependency injection

```csharp
using Line.Liff.DependencyInjection;

services.AddLineLiff(o => o.ChannelAccessToken = "CHANNEL_ACCESS_TOKEN");
// resolve: sp.GetRequiredService<LiffClient>()
```

See [Dependency Injection & Hosting](di-and-hosting.md) for the auth-provider overload (for
example to inject a refreshing token provider).
