# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

日本語版は [`CHANGELOG_ja.md`](CHANGELOG_ja.md) を参照してください。

This repository publishes three independently versioned release lines:

- **Libraries** — the `Line.OpenApi.*` client packages, tagged `v*`.
- **Tools** — the `Line.OpenApi.Tools` CLI / MCP global tool (command `line`), tagged `tools-v*`.
- **AI Tools** — the `Line.OpenApi.Extensions.AI` in-process Microsoft.Extensions.AI tools, tagged `ai-v*`.

They evolve on separate cadences, so each has its own version history below.

---

## Tools — `Line.OpenApi.Tools`

### [1.3.0] - 2026-09-04

Lets the Flex preview show your own local artwork and video without hosting.

#### Added

- **Local media serving for the Flex preview (`LINE_FLEX_MCP_ASSET_DIR`).** Set this environment variable to a folder and the loopback preview server serves media files from it, so a Flex message can reference them by a **relative** `url` (e.g. `assets/hero.png`) while designing — then swap only the origin for the production HTTPS URL later (the relative path stays the same). The served set matches what LINE renders in a Flex message: **JPEG/PNG** (APNG is `.png`) images and **`.mp4`** for the `video` component (other formats such as GIF/WebP are intentionally not served). Serving is **opt-in** (disabled unless the folder is set), confined to that folder (path traversal and out-of-directory symlinks are rejected), loopback-only, and read-only-safe. LINE itself renders neither local nor `data:` URLs, so this is a preview convenience.

### [1.2.0] - 2026-09-03

Adds a live, LINE-faithful Flex Message preview to the tool.

#### Added

- **Flex Message live preview (`line_flex_*`).** New read-only MCP tools `line_flex_preview` / `line_flex_get_content` / `line_flex_validate` / `line_flex_open` render Flex JSON in a loopback browser view that hot-updates as you iterate, and read back edits made in the browser. No LINE API calls or secrets are involved, so the tools are available under `--read-only`. The same renderer also ships as a Copilot App canvas extension under `extensions/line-flex-viewer/` (with a bundled zero-dependency Node MCP server as an alternative).

### [1.1.0] - 2026-08-13

Automates repointing a dev tunnel without visiting the LINE Developers console.

#### Added

- **Webhook endpoint configuration.** CLI `line webhook get-endpoint` / `set-endpoint --url <url>` / `test-endpoint [--url <url>]`; MCP `line_webhook_get_endpoint` and `line_webhook_test_endpoint` (read-only), `line_webhook_set_endpoint` (mutating). `test-endpoint` asks the LINE platform to probe the endpoint and reports reachability.
- **LIFF endpoint URL update.** CLI `line liff update-url <liffId> --url <url>`; MCP `line_liff_update_url`. Updates only `view.url` via a partial update. Use `line liff list` to find the `liffId`.
- URLs for the new set/test/update commands are required to be absolute **https** and are rejected before any network call.

### [1.0.0] - 2026-08-12

First stable (GA) release.

#### Added

- Full CLI / MCP surface: `config`, `token`, `message`, `bot`, `webhook` (verify / replay / listen), `liff`, `richmenu`, `insight`, `audience`, `shop`.
- MCP server (`line mcp`) exposing the same operations as `line_<area>_<verb>` tools (except `webhook listen`), with `--read-only`, `--allow-secret-output` (`line_token_issue` withholds the raw token by default), and `--allow-remote-replay` (loopback-only by default) safety flags.
- Message assembly aids for the flagship "build → dry-run → send" loop: `line_message_schema` and a `dryRun` argument on the send tools.

### [0.2.0-preview] - 2026-07-16

#### Added

- Rich menu command group (`line richmenu`, incl. image upload/download) and MCP tools.
- Exposure of the Insight / Manage Audience / Shop coverage packages (`line insight` / `line audience` / `line shop`).

### [0.1.0-preview] - 2026-07-14

- Initial public release of the CLI / MCP tool (first published as `Line.OpenApi.Cli`, then renamed to `Line.OpenApi.Tools`). Surface: `config`, `token`, `message`, `bot`, `webhook`, `liff`.

---

## Libraries — `Line.OpenApi.*`

### 1.0.0 - 2026-08-12

First stable (GA) release of the client libraries.

- Package set: `Line.OpenApi.Core`, `.ChannelAccessToken`, `.Messaging`, `.Messaging.Webhook`, `.Liff`, `.Insight`, `.ManageAudience`, `.Module`, `.Shop`, `.Login`, `.MiniApp`, and the `.Bot` meta-package.
- Target framework `net10.0`. Kiota-generated clients over the public [line-openapi](https://github.com/line/line-openapi) specs, plus hand-written facades, DI extensions, and webhook-receiving glue.

### 0.2.0-preview - 2026-07-16

#### Added

- Coverage packages `Line.OpenApi.Insight`, `.ManageAudience`, `.Module`, `.Shop`, and the hand-written `Line.OpenApi.Login`.
- `RichMenuClient` helpers in `Line.OpenApi.Messaging`.

### 0.1.0-preview - 2026-07-14

- First public release to NuGet.org: `Line.OpenApi.Core`, `.ChannelAccessToken`, `.Messaging`, `.Messaging.Webhook`, `.Liff`, and the `.Bot` meta-package.

---

## AI Tools — `Line.OpenApi.Extensions.AI`

### 1.0.0 - 2026-08-20

First stable release of the in-process AI tools package: the LINE Messaging use case exposed as [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/) `AIFunction` tools, usable from Semantic Kernel or any Microsoft.Extensions.AI host.

#### Added

- `LineMessagingAiTools.CreateReadOnly` / `Create` producing the tools: read-only `line_bot_info` / `line_bot_quota` / `line_bot_profile` / `line_message_validate`, plus opt-in `line_message_push` / `line_message_multicast` / `line_message_reply` / `line_message_broadcast`.
- Safe-by-default sending model via `LineAiToolOptions`: `EnableSending`, `AllowBroadcast`, `DryRun`, a `SendPolicy` blast-radius gate, and a human-in-the-loop `BeforeSend` hook. None of the gates is exposed as a tool argument, so a model cannot flip them.
- Two dependencies only: `Line.OpenApi.Messaging` and `Microsoft.Extensions.AI.Abstractions`.

---

[1.2.0]: https://github.com/pierre3/line-openapi-dotnet/compare/tools-v1.1.0...tools-v1.2.0
[1.1.0]: https://github.com/pierre3/line-openapi-dotnet/compare/tools-v1.0.0...tools-v1.1.0
[1.0.0]: https://github.com/pierre3/line-openapi-dotnet/compare/tools-v0.2.0-preview...tools-v1.0.0
[0.2.0-preview]: https://github.com/pierre3/line-openapi-dotnet/compare/tools-v0.1.0-preview...tools-v0.2.0-preview
[0.1.0-preview]: https://github.com/pierre3/line-openapi-dotnet/releases/tag/tools-v0.1.0-preview
