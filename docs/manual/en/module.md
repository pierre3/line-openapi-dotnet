# Module Channels

@Line.OpenApi.Module.ModuleClient is the facade for the Module channel API, used by partners /
agencies operating a LINE Official Account on the owner's behalf (LOA). It uses a single host
(`api.line.me`) and provides convenience methods for the four operations: detach, acquire /
release chat control, and list attached modules. For anything lower-level, `ModuleClient.Api`
exposes the generated builders directly.

> **Not included:** module attachment (`module-attach`, on `manager.line.biz` with HTTP Basic
> auth and PKCE) is out of scope for this package. It may be added when there is concrete demand.

```csharp
using Line.OpenApi.Module;
using Line.OpenApi.Module.Generated.Models;

var module = ModuleClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
```

## List attached modules

```csharp
// start: pagination token, limit: max bots (LINE default 100).
var modules = await module.GetModulesAsync(start: null, limit: 100);
```

## Acquire / release chat control

```csharp
await module.AcquireChatControlAsync("CHAT_ID", new AcquireChatControlRequest
{
    Expired = true,
    Ttl = 3600,
});

await module.ReleaseChatControlAsync("CHAT_ID");
```

## Detach

```csharp
await module.DetachAsync(new DetachModuleRequest { BotId = "BOT_ID" });
```

## Dependency injection

```csharp
using Line.OpenApi.Module.DependencyInjection;

services.AddLineModule(o => o.ChannelAccessToken = "CHANNEL_ACCESS_TOKEN");
// resolve: sp.GetRequiredService<ModuleClient>()
```

See [Dependency Injection & Hosting](di-and-hosting.md) for the auth-provider overload.
