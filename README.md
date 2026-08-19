**English** | [日本語](https://github.com/pierre3/line-openapi-dotnet/blob/main/README_ja.md)

# LINE .NET client (Line.OpenApi.*)

[![CI](https://github.com/pierre3/line-openapi-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/pierre3/line-openapi-dotnet/actions/workflows/ci.yml)
[![Docs](https://img.shields.io/badge/docs-GitHub%20Pages-blue)](https://pierre3.github.io/line-openapi-dotnet/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/pierre3/line-openapi-dotnet/blob/main/LICENSE)
[![NuGet](https://img.shields.io/badge/NuGet-Line.OpenApi.*-004880?logo=nuget)](https://www.nuget.org/packages?q=tags%3A%22LINE-API%22)

A set of .NET/C# client libraries generated from the official LINE OpenAPI specifications with [Kiota](https://learn.microsoft.com/openapi/kiota/), layered with hand-written facades / DI / receive glue organized by usage scenario.

- Supports **messaging (Bot)** and **LIFF app management** as the primary use cases
- Automatically routes the two hosts — control plane (`api.line.me`) and data plane (`api-data.line.me`) — through the `MessagingClient` facade
- Consolidates webhook receiving (signature verification + deserialization) in `WebhookRequestParser`
- DI integration based on `IHttpClientFactory`

The target framework is **`net10.0` only** (netstandard2.0 / .NET Framework are out of scope).

## Packages

| Package | Role |
|---|---|
| `Line.OpenApi.Core` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.Core.svg)](https://www.nuget.org/packages/Line.OpenApi.Core) | Common foundation (authentication providers, webhook signature verification, allowed hosts) |
| `Line.OpenApi.ChannelAccessToken` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.ChannelAccessToken.svg)](https://www.nuget.org/packages/Line.OpenApi.ChannelAccessToken) | Channel access token issuance (v2.1 JWT / v3 stateless, refreshing provider) |
| `Line.OpenApi.Messaging` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.Messaging.svg)](https://www.nuget.org/packages/Line.OpenApi.Messaging) | Messaging (`MessagingClient` facade = control-plane + data-plane clients unified) |
| `Line.OpenApi.Messaging.Webhook` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.Messaging.Webhook.svg)](https://www.nuget.org/packages/Line.OpenApi.Messaging.Webhook) | Webhook models + receive glue (`WebhookRequestParser` = signature verification + deserialization) |
| `Line.OpenApi.Liff` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.Liff.svg)](https://www.nuget.org/packages/Line.OpenApi.Liff) | LIFF app management (`LiffClient` facade) |
| `Line.OpenApi.Login` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.Login.svg)](https://www.nuget.org/packages/Line.OpenApi.Login) | LINE Login v2.1 + OpenID Connect (`LoginClient` facade = authorization URL / token exchange / ID-token & access-token verification / profile / friendship) |
| `Line.OpenApi.MiniApp` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.MiniApp.svg)](https://www.nuget.org/packages/Line.OpenApi.MiniApp) | LINE MINI App service messages + in-app purchase (`MiniAppClient` facade = notification token issue/send, IAP product reservation, IAP webhook event history) |
| `Line.OpenApi.Insight` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.Insight.svg)](https://www.nuget.org/packages/Line.OpenApi.Insight) | Insight / statistics (`InsightClient` facade = friend demographics, deliveries, followers, message events, rich menu insights) |
| `Line.OpenApi.ManageAudience` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.ManageAudience.svg)](https://www.nuget.org/packages/Line.OpenApi.ManageAudience) | Audience management (`ManageAudienceClient` facade = create/get/list/delete audience groups, click/imp retargeting, by-file user-ID upload on the data plane) |
| `Line.OpenApi.Module` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.Module.svg)](https://www.nuget.org/packages/Line.OpenApi.Module) | Module channels for partner/agency operation (`ModuleClient` facade = detach, chat control, list attached modules) |
| `Line.OpenApi.Shop` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.Shop.svg)](https://www.nuget.org/packages/Line.OpenApi.Shop) | Mission sticker sending (`ShopClient` facade) |
| `Line.OpenApi.Bot` [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.Bot.svg)](https://www.nuget.org/packages/Line.OpenApi.Bot) | Convenience meta-package (optional) = the full Bot set in a single reference (bundles `Messaging` + `Messaging.Webhook` + `ChannelAccessToken`; no code, dependencies only) |

## Installation

> All packages are published on NuGet.org (currently `1.0.0`). See [all `Line.OpenApi.*` packages](https://www.nuget.org/packages?q=tags%3A%22LINE-API%22).

```sh
# Install the full Bot set (send + receive + token issuance) at once
dotnet add package Line.OpenApi.Bot

# Or install per usage scenario
dotnet add package Line.OpenApi.Messaging
dotnet add package Line.OpenApi.Liff
dotnet add package Line.OpenApi.Login
dotnet add package Line.OpenApi.MiniApp
dotnet add package Line.OpenApi.Insight
dotnet add package Line.OpenApi.ManageAudience
dotnet add package Line.OpenApi.Module
dotnet add package Line.OpenApi.Shop
```

## Requirements

- .NET SDK 10 or later (check with `dotnet --version`)

## Usage

### Sending messages (`Line.OpenApi.Messaging`)

```csharp
using Line.OpenApi.Messaging;
using Line.OpenApi.Messaging.Generated.Api.Models;

// Quick construction (long-lived channel access token)
var client = MessagingClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");

await client.Api.V2.Bot.Message.Push.PostAsync(new PushMessageRequest
{
    To = "U0123456789abcdef...",
    Messages = new()
    {
        new TextMessage { Text = "Hello, world" },
    },
});

// Content retrieval is automatically routed to the data plane (api-data.line.me)
var stream = await client.Blob.V2.Bot.Message["<messageId>"].Content.GetAsync();
```

DI (recommended: handler sharing via `IHttpClientFactory`, CVE-fixed middleware applied):

```csharp
using Line.OpenApi.Messaging.DependencyInjection;

services.AddLineMessaging(o => o.ChannelAccessToken = "CHANNEL_ACCESS_TOKEN");
// Resolve: sp.GetRequiredService<MessagingClient>()
```

To use short-lived tokens (e.g. v2.1 JWT assertion), pass a refreshing provider through the authentication-provider injection path:

```csharp
services.AddLineMessaging(sp => /* return an IAuthenticationProvider (e.g. the refreshing provider from Line.OpenApi.ChannelAccessToken) */);
```

### LIFF app management (`Line.OpenApi.Liff`)

```csharp
using Line.OpenApi.Liff;
using Line.OpenApi.Liff.Generated.Models;

var liff = LiffClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");

var apps  = await liff.GetAppsAsync();
var added = await liff.AddAppAsync(new AddLiffAppRequest
{
    View = new LiffView { Type = LiffView_type.Full, Url = "https://example.com" },
});
await liff.UpdateAppAsync(added!.LiffId!, new UpdateLiffAppRequest { Description = "updated" });
await liff.DeleteAppAsync(added.LiffId!);
```

DI: `services.AddLineLiff(o => o.ChannelAccessToken = "…");`

### LINE Login + OpenID Connect (`Line.OpenApi.Login`)

`LoginClient` covers the browser authorization-code flow (with optional PKCE) and its follow-ups. Unlike Messaging, LINE Login authenticates with a **user access token** (a different credential system from the Messaging channel access token); token issuance uses the LINE Login **channel ID + channel secret**.

```csharp
using Line.OpenApi.Login;

var login = new LoginClient("LOGIN_CHANNEL_ID", "LOGIN_CHANNEL_SECRET");

// 1) Redirect the browser to the authorization URL (build only; no HTTP call).
var pkce  = LineLoginSecurity.CreatePkceChallenge();
var state = LineLoginSecurity.GenerateState();          // store state + pkce.CodeVerifier in the session
var url   = login.BuildAuthorizationUrl(new AuthorizationUrlParameters
{
    RedirectUri   = "https://app.example.com/callback",
    Scopes        = new[] { "openid", "profile" },
    State         = state,
    Nonce         = "server-generated-nonce",
    CodeChallenge = pkce.CodeChallenge,
});

// 2) On the callback (after verifying state), exchange the code for tokens.
var token = await login.ExchangeCodeAsync("<code>", "https://app.example.com/callback", pkce.CodeVerifier);

// 3) Verify the ID token (delegated to LINE) and read the profile with the user access token.
var claims  = await login.VerifyIdTokenAsync(token!.IdToken!, nonce: "server-generated-nonce");
var profile = await login.GetProfileAsync(token.AccessToken!);
var friend  = await login.GetFriendshipStatusAsync(token.AccessToken!);   // friend.FriendFlag
```

DI: `services.AddLineLogin(o => { o.ChannelId = "…"; o.ChannelSecret = "…"; });`

> Local ID-token verification (HS256 for web / ES256 + JWKS for native/LIFF) is not included in this release; use `VerifyIdTokenAsync` (server-side delegation) for now.

### LINE MINI App (`Line.OpenApi.MiniApp`)

`MiniAppClient` covers two independent feature areas on `api.line.me`, both hand-written (LINE publishes no OpenAPI spec for MINI App). Tokens are passed per call, not stored, so this package depends on neither `Line.OpenApi.ChannelAccessToken` nor `Line.OpenApi.Login`.

```csharp
using Line.OpenApi.MiniApp;

var miniApp = new MiniAppClient();

// Service messages: notify a user in response to an action they took in the MINI App.
// liffAccessToken comes from the front-end's liff.getAccessToken().
var issued = await miniApp.IssueNotificationTokenAsync("CHANNEL_ACCESS_TOKEN", liffAccessToken);
var sent = await miniApp.SendServiceMessageAsync(
    "CHANNEL_ACCESS_TOKEN", issued!.NotificationToken!, "order-complete_en",
    new Dictionary<string, string> { ["orderName"] = "Widget" });
// sent.NotificationToken is renewed on every send; save it for the next call.

// In-app purchase (IAP): reserve with the purchasing user's user access token.
var reserved = await miniApp.ReserveProductAsync(
    userAccessToken, clientIp: "203.0.113.1", clientOs: "ios",
    productId: "PRODUCT1", shopProductName: "Gold Pack");

// Read the platform's purchase/refund webhook history (past 7 days, cursor-paginated).
var events = await miniApp.GetWebhookEventsAsync(
    "CHANNEL_ACCESS_TOKEN", startEpochSeconds, endEpochSeconds, pageSize: 50);
```

DI: `services.AddLineMiniApp();` (no required configuration; pass `o => o.AllowedHosts = …` only to override the default host allow list).

### Insight / statistics (`Line.OpenApi.Insight`)

```csharp
using Line.OpenApi.Insight;

var insight = InsightClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
var followers = await insight.GetNumberOfFollowersAsync("20260715");   // yyyyMMdd
var summary = await insight.GetRichMenuInsightSummaryAsync("RICH_MENU_ID", "20260701", "20260715");
```

DI: `services.AddLineInsight(o => o.ChannelAccessToken = "…");`

### Manage Audience (`Line.OpenApi.ManageAudience`)

Control plane (`api.line.me`) + data plane (`api-data.line.me`). The by-file upload is wrapped so you don't build the multipart body yourself.

```csharp
using Line.OpenApi.ManageAudience;

var ma = ManageAudienceClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
using var file = File.OpenRead("user-ids.txt");   // one user ID / IFA per line
var created = await ma.UploadUserIdsByFileAsync(file, description: "my audience");
await ma.AddUserIdsByFileAsync(created!.AudienceGroupId!.Value, File.OpenRead("more-ids.txt"));
```

DI: `services.AddLineManageAudience(o => o.ChannelAccessToken = "…");`

### Module channels (`Line.OpenApi.Module`)

```csharp
using Line.OpenApi.Module;

var module = ModuleClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
var modules = await module.GetModulesAsync(limit: 100);
await module.ReleaseChatControlAsync("CHAT_ID");
```

DI: `services.AddLineModule(o => o.ChannelAccessToken = "…");`
Module attachment (`module-attach`, on `manager.line.biz` with Basic auth + PKCE) is out of scope for this package.

### Mission stickers (`Line.OpenApi.Shop`)

```csharp
using Line.OpenApi.Shop;
using Line.OpenApi.Shop.Generated.Models;

var shop = ShopClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");
await shop.SendMissionStickerAsync(new MissionStickerRequest
{
    To = "USER_ID", ProductType = "STICKER", ProductId = "PRODUCT_ID",
});
```

DI: `services.AddLineShop(o => o.ChannelAccessToken = "…");`

### Receiving webhooks (`Line.OpenApi.Messaging.Webhook`)

`WebhookRequestParser` bundles **signature verification (`x-line-signature`) + body deserialization** into a single call. It throws `WebhookSignatureException` on a bad signature and `WebhookPayloadException` on a malformed body (both derive from `WebhookException`).

```csharp
using Line.OpenApi.Messaging.Webhook.DependencyInjection;

services.AddLineWebhook(o => o.ChannelSecret = "CHANNEL_SECRET");
// Resolve: sp.GetRequiredService<WebhookRequestParser>()
```

Receiving example in ASP.NET Core (**reading the raw body and extracting the signature header is the caller's responsibility**; the signature is verified against the raw bytes, so read the raw body before model binding):

```csharp
using Line.OpenApi.Messaging.Webhook;
using Line.OpenApi.Messaging.Webhook.Generated.Models;

app.MapPost("/webhook", async (HttpRequest request, WebhookRequestParser parser) =>
{
    using var ms = new MemoryStream();
    await request.Body.CopyToAsync(ms);
    var body = ms.ToArray();                                  // raw bytes to verify against
    var signature = request.Headers["x-line-signature"];

    CallbackRequest callback;
    try
    {
        callback = await parser.ParseAsync(body, signature);
    }
    catch (WebhookSignatureException) { return Results.Unauthorized(); }  // bad signature
    catch (WebhookPayloadException)   { return Results.BadRequest(); }    // bad body

    // Events are restored to concrete types by the type discriminator (unknown types stay as the base Event).
    // Branching from here is up to the caller:
    foreach (var ev in callback.Events!)
    {
        switch (ev)
        {
            case MessageEvent m when m.Message is TextMessageContent t:
                Console.WriteLine($"text: {t.Text}");
                break;
            case FollowEvent:                 /* friend added */        break;
            case PostbackEvent p:             /* p.Postback!.Data */    break;
            // Unknown events arrive as the base Event type (may be ignored)
        }
    }
    return Results.Ok();
});
```

> For multi-tenant scenarios (a different secret per channel), use the static overload
> `WebhookRequestParser.ParseAsync(channelSecret, body, signature)`.
>
> A maximum body size (DoS protection) is out of scope for this helper. Enforce a raw-body size
> limit upstream, e.g. ASP.NET Core's `MaxRequestBodySize`.

## CLI / MCP tool (`line`)

A CLI / MCP tool `line` (`Line.OpenApi.Tools`) for operating LINE from your local machine is included under `tools/`. It provides token issuance, message send, webhook development helpers, and LIFF management both as **CLI subcommands** and as **MCP server tools** (usable from Claude Desktop / Claude Code).

```sh
dotnet tool install -g Line.OpenApi.Tools   # after publishing
line message push --to <id> --text "Hello"
line mcp                                   # start as an MCP server
```

See [`tools/README.md`](https://github.com/pierre3/line-openapi-dotnet/blob/main/tools/README.md) ([日本語](https://github.com/pierre3/line-openapi-dotnet/blob/main/tools/README_ja.md)) for details.

## AI tools (`Line.OpenApi.Extensions.AI`)

`Line.OpenApi.Extensions.AI` wraps the Messaging use case as [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/) `AIFunction` tools, so an LLM agent built on Semantic Kernel or any Microsoft.Extensions.AI host can operate LINE **in-process** (this complements the CLI/MCP tool, which runs out-of-process). It depends only on `Line.OpenApi.Messaging` and `Microsoft.Extensions.AI.Abstractions`.

```csharp
using Line.OpenApi.Extensions.AI;
using Line.OpenApi.Messaging;

var line = MessagingClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");

// Safe by default: read-only tools only (bot info / quota / profile / message-validate).
IReadOnlyList<AIFunction> readTools = LineMessagingAiTools.CreateReadOnly(line);

// Sending is explicit opt-in and gated.
IReadOnlyList<AIFunction> tools = LineMessagingAiTools.Create(line, new LineAiToolOptions
{
    EnableSending = true,                 // enables push / multicast / reply (default false)
    AllowBroadcast = false,               // broadcast = largest blast radius, separate opt-in
    SendPolicy = (ctx, ct) =>             // bound blast radius (operation / recipients / count)
        new(ctx.Operation != LineSendOperation.Broadcast),
    BeforeSend = (ctx, ct) => /* human-in-the-loop / audit; inspect ctx.MessagesJson */ new(true),
});

// Semantic Kernel consumes Microsoft.Extensions.AI functions directly:
// kernel.Plugins.AddFromFunctions("Line", tools);
```

**Safety model.** Sends are off by default; broadcast needs its own opt-in; `SendPolicy` and `BeforeSend` gate every send and are set by you at creation time — they are **never** exposed as tool arguments, so a model cannot flip them. Tool results are non-secret and the channel access token never appears in a result, description, or exception. Rate / cumulative-count limiting is the host pipeline's responsibility. Note that message content is passed to `SendPolicy` / `BeforeSend` and retained on `LineSendContext.MessagesJson` (including on a `LineSendRefusedException`), and read-tool results (profile display name, etc.) flow to your LLM provider — so treat tool arguments and results as potential PII in your logs and audit trail.

## Samples

Runnable demo apps are included under `samples/` (not part of the NuGet packages). They are **offline by default** and connect to the real API when environment variables are set.

- **`Line.OpenApi.Samples.Console`** — send / LIFF management / token issuance / webhook parsing (`dotnet run -- webhook` works without credentials)
- **`Line.OpenApi.Samples.Webhook`** — minimal API webhook receiver + echo reply (live demo via a dev tunnel)
- **`Line.OpenApi.Samples.Login`** — LINE Login + OpenID Connect authorization-code flow (PKCE) with localhost callback
- **`Line.OpenApi.Samples.Ai`** — LLM tool-calling: a scripted model drives the `Line.OpenApi.Extensions.AI` tools through the safety gates (runs fully offline)

See [`samples/README.md`](https://github.com/pierre3/line-openapi-dotnet/blob/main/samples/README.md) for run steps, environment variables, and dev tunnel setup.

## Tutorial

For a hands-on, end-to-end walkthrough, see the companion project
**[pierre3/line-companion-bot](https://github.com/pierre3/line-companion-bot)** — a virtual
companion-raising LINE bot combined with a LINE MINI App shop, built to exercise the
`Line.OpenApi.*` libraries as a whole (Messaging / Webhook / Rich Menu / MINI App service messages +
In-App Purchase). Its bilingual tutorial builds the app one chapter at a time, from `dotnet new` to
the full end-to-end loop, and covers LINE Developers Console setup, dev tunnel configuration, and
the rich-menu registration flow via the `line` CLI.

- Tutorial (English): [`docs/manual/en/`](https://github.com/pierre3/line-companion-bot/blob/main/docs/manual/en/README.md)
- Tutorial (日本語): [`docs/manual/ja/`](https://github.com/pierre3/line-companion-bot/blob/main/docs/manual/ja/README.md)

## Showcase

Apps built with these libraries:

- **[pierre3/line-hf-bot](https://github.com/pierre3/line-hf-bot)** — a LINE bot for AI chat, image generation/editing, and video generation with Hugging Face models (ASP.NET, .NET 10). Built on the `Line.OpenApi.Bot` package (Messaging / Webhook / Rich Menu) and shipped as a Docker image.

## Build from source

At the repository root:

```sh
dotnet build            # net10.0 only
dotnet test             # runs everything by default, including webhook polymorphism (no opt-in flag)
```

### Regenerating from the spec (optional)

The Kiota CLI is only needed if you regenerate the clients from the OpenAPI specs (bundled under `openapi/`):

```sh
dotnet tool install --global Microsoft.OpenApi.Kiota

./scripts/generate.ps1        # Windows / PowerShell
bash scripts/generate.sh      # macOS / Linux
```

Generated code lives under `src/**/Generated/` (`kiota-lock.json` is committed). The `Microsoft.Kiota.Bundle` version is managed centrally via `KiotaBundleVersion` in `Directory.Build.props` (currently 2.0.0).

### Staying in sync with upstream

The bundled specs under `openapi/` are a pinned snapshot of [`line/line-openapi`](https://github.com/line/line-openapi). Upstream publishes no tags/releases and its `info.version` fields are static, so the ingested revision is anchored by **upstream commit SHA** in `openapi/upstream-manifest.json` (ref + retrieval date + per-spec LF-normalized sha256).

A weekly workflow ([`.github/workflows/spec-sync.yml`](.github/workflows/spec-sync.yml)) detects drift, opens a tracking issue, and files a **draft PR** with the regenerated client. Merging is always human-gated. To run the pieces locally:

```sh
pwsh scripts/check-spec-drift.ps1        # report drift vs the manifest (exit 1 if drifted)
pwsh scripts/generate.ps1 -Update        # re-fetch at the upstream HEAD, normalize, update manifest, regenerate
```

Line endings are normalized to LF before hashing/diffing (see `.gitattributes`); comparing raw bytes against the LF upstream would otherwise report whole-file false drift.

## Documentation

- **📖 User manual (published): https://pierre3.github.io/line-openapi-dotnet/** — conceptual articles (English / Japanese) plus the English API reference.
- Changelog: [`CHANGELOG.md`](https://github.com/pierre3/line-openapi-dotnet/blob/main/CHANGELOG.md)
- Design: [`docs/LINE-dotnet-client-design.md`](https://github.com/pierre3/line-openapi-dotnet/blob/main/docs/LINE-dotnet-client-design.md)
- The manual is generated with [DocFX](https://dotnet.github.io/docfx/) into `docs/manual/` and published to GitHub Pages by the `docs` workflow. DocFX is pinned as a local tool (`.config/dotnet-tools.json`); build it locally with:

```sh
dotnet tool restore                              # first time only (restores DocFX)
dotnet docfx docs/manual/docfx.json              # metadata extraction + site build → docs/manual/_site/
dotnet docfx docs/manual/docfx.json --serve      # local preview (http://localhost:8080)
```

The API reference is auto-generated in English from the XML doc comments on the hand-written public surface (generated `Line.*.Generated` is excluded via `filterConfig.yml`). Generated artifacts (`docs/manual/api/`, `docs/manual/_site/`) are not tracked by Git. See design §13 for details.

## Project layout

```
(repository root)
├── LineOpenApi.slnx             # solution
├── Directory.Build.props        # shared TFM (net10.0) / nullable / Kiota version
├── openapi/                     # spec snapshots
├── scripts/                     # Kiota generation & package verification scripts
├── src/
│   ├── Line.OpenApi.Core/               # auth providers, signature verification, allowed hosts (hand-written)
│   ├── Line.OpenApi.ChannelAccessToken/ # token issuance (form-urlencoded generation + hand-written helpers)
│   ├── Line.OpenApi.Messaging/          # control-plane + data-plane clients + MessagingClient facade
│   ├── Line.OpenApi.Messaging.Webhook/  # webhook models + WebhookRequestParser (receive glue)
│   ├── Line.OpenApi.Liff/               # LIFF + LiffClient facade
│   ├── Line.OpenApi.Login/              # LINE Login v2.1 + OIDC (hand-written, no spec) + LoginClient facade
│   ├── Line.OpenApi.MiniApp/            # MINI App service messages + IAP (hand-written, no spec) + MiniAppClient facade
│   ├── Line.OpenApi.Insight/            # Insight / statistics + InsightClient facade
│   ├── Line.OpenApi.ManageAudience/     # audience management (control + data plane) + ManageAudienceClient facade
│   ├── Line.OpenApi.Module/             # Module channels + ModuleClient facade
│   ├── Line.OpenApi.Shop/               # Mission stickers + ShopClient facade
│   └── Line.OpenApi.Bot/                # convenience meta-package (dependencies only, no code)
├── tools/
│   ├── Line.OpenApi.Tools/              # CLI / MCP tool (command name `line`)
│   ├── Line.OpenApi.Extensions.AI/      # Microsoft.Extensions.AI tools for LLM tool-calling
│   └── shared/                          # source shared between Tools and Extensions.AI (not a package)
├── samples/                     # bundled demo apps (console / webhook Web API)
├── tests/                       # tests for the hand-written surface (signature/receive/routing/DI/snapshot, etc.)
└── docs/                        # design, review records, user manual (manual/)
```

## License

[MIT](https://github.com/pierre3/line-openapi-dotnet/blob/main/LICENSE) © pierre3
