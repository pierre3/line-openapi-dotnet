# LINE .NET Client Library

A set of .NET/C# client libraries for the [LINE public OpenAPI specification](https://github.com/line/line-openapi),
generated with [Kiota](https://learn.microsoft.com/openapi/kiota/) and layered with
hand-written facades, dependency-injection helpers, and webhook receive glue organized by
use case.

- **Target framework:** `net10.0` only (netstandard2.0 / .NET Framework are out of scope).
- **API reference language:** English (generated from XML doc comments).
- **Guides:** available in **English** and **日本語**.

## Choose your path

| | |
|---|---|
| 📘 **[Guide (English)](en/getting-started.md)** | Tutorials and conceptual articles in English. |
| 📗 **[ガイド (日本語)](ja/getting-started.md)** | 日本語のチュートリアルと概念記事。 |
| 📚 **[API Reference](xref:Line.OpenApi.Messaging)** | Auto-generated reference for the hand-written public surface. |

## Packages

| Package | Role |
|---|---|
| `Line.OpenApi.Core` | Shared foundation (auth providers, webhook signature validation, allowed hosts). |
| `Line.OpenApi.ChannelAccessToken` | Channel-access-token issuance (v2.1 JWT / v3 stateless, refreshing provider). |
| `Line.OpenApi.Messaging` | Message send & receive (`MessagingClient` facade = control plane + data plane). |
| `Line.OpenApi.Messaging.Webhook` | Webhook models + receive glue (`WebhookRequestParser` = signature check + deserialization). |
| `Line.OpenApi.Liff` | LIFF app management (`LiffClient` facade). |
| `Line.OpenApi.Login` | LINE Login v2.1 + OpenID Connect (`LoginClient` facade). |
| `Line.OpenApi.MiniApp` | LINE MINI App service messages + in-app purchase (`MiniAppClient` facade). |
| `Line.OpenApi.Insight` | Insight / statistics (`InsightClient` facade). |
| `Line.OpenApi.ManageAudience` | Audience management incl. by-file upload (`ManageAudienceClient` facade = control plane + data plane). |
| `Line.OpenApi.Module` | Module channels for partner/agency operation (`ModuleClient` facade). |
| `Line.OpenApi.Shop` | Mission sticker sending (`ShopClient` facade). |
| `Line.OpenApi.Bot` | Optional meta-package bundling Messaging + Webhook + ChannelAccessToken. |

> The API reference documents only the **hand-written public surface**. The Kiota-generated
> request builders and models are treated as an opaque box; you use them through the fluent
> builder paths shown in the guides (for example `client.Api.V2.Bot.Message.Push.PostAsync`).

<!-- API Reference browsing is available from the top navigation bar. -->

