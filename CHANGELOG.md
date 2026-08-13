# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

日本語版は [`CHANGELOG_ja.md`](CHANGELOG_ja.md) を参照してください。

This repository publishes two independently versioned release lines:

- **Libraries** — the `Line.OpenApi.*` client packages, tagged `v*`.
- **Tools** — the `Line.OpenApi.Tools` CLI / MCP global tool (command `line`), tagged `tools-v*`.

They evolve on separate cadences, so each has its own version history below.

---

## Tools — `Line.OpenApi.Tools`

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

[1.1.0]: https://github.com/pierre3/line-openapi-dotnet/compare/tools-v1.0.0...tools-v1.1.0
[1.0.0]: https://github.com/pierre3/line-openapi-dotnet/compare/tools-v0.2.0-preview...tools-v1.0.0
[0.2.0-preview]: https://github.com/pierre3/line-openapi-dotnet/compare/tools-v0.1.0-preview...tools-v0.2.0-preview
[0.1.0-preview]: https://github.com/pierre3/line-openapi-dotnet/releases/tag/tools-v0.1.0-preview
