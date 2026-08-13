**English** | [日本語](https://github.com/pierre3/line-openapi-dotnet/blob/main/tools/README_ja.md)

# Line.OpenApi.Tools — CLI / MCP tool for LINE

A **`dotnet` global tool** (command name `line`) for operating the LINE Platform from your local machine. Built on top of the `Line.OpenApi.*` client libraries, it exposes **the same functionality both as CLI subcommands and as MCP server tools**.

- **CLI** — run from a terminal by a human (`line message push ...`)
- **MCP server** — called by AI agents (Claude Desktop / Claude Code, etc.) via `line mcp` over stdio

Both share the same service layer, so behavior is identical.

## Features

| Area | Capabilities |
|---|---|
| **A. Token management** | Issue (v2.1 JWT / stateless), verify, and revoke channel access tokens |
| **B. Message send & bot lookup** | push / multicast / broadcast / reply, bot info / quota / consumption, user profile, message content download |
| **C. Webhook development** | Local receiver (with signature verification), offline signature verification of a stored payload, replay to a local app, webhook endpoint get/set/test (repoint at a dev tunnel) |
| **D. LIFF management** | List / add / update / delete LIFF apps, update endpoint URL only (`view.url`) |
| **E. Rich menu** | Create / validate / list / get / delete, image upload & download, set / cancel default, link / unlink per user |
| **F. Insight / Audience / Shop** | Statistics (demographics, deliveries, followers, events, rich menu); audience group manage (incl. by-file user-id upload); mission sticker send |

Target framework: **`net10.0`**.

---

## Installation

> Published on NuGet.org: [![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.Tools.svg)](https://www.nuget.org/packages/Line.OpenApi.Tools)

```sh
dotnet tool install -g Line.OpenApi.Tools
```

Run from local source (inside this repository):

```sh
dotnet run --project tools/Line.OpenApi.Tools -- <command> ...
# e.g.
dotnet run --project tools/Line.OpenApi.Tools -- --help
```

---

## Quick start

### 1. Configure credentials (profiles)

Credentials are stored per **profile** in `~/.line/config.json` (`%USERPROFILE%\.line\config.json` on Windows).

```sh
# Fastest start with a static token
line config set default --token "YOUR_CHANNEL_ACCESS_TOKEN"

# Or set the key material used for token issuance
line config set default \
  --channel-id "1234567890" \
  --kid "your-key-id" \
  --private-key "~/.line/keys/default.pem" \
  --secret "YOUR_CHANNEL_SECRET"

line config list          # list profiles (* marks the default)
line config get default   # show a profile (secrets masked)
line config use staging   # switch the default profile
```

> **Security**: the config stores secrets in plain text. On Unix it is created with `0600` (owner read/write only). On Windows it inherits the user-profile ACL (a warning is printed on save). The private key is referenced **by path only** — its contents are never stored in the config.

### 2. Try it

```sh
line bot info                                   # bot information
line message push --to U0123... --text "Hello"  # push a message
line liff list                                  # list LIFF apps
```

---

## Credential resolution order

Each command resolves credentials in the following priority (**top wins**):

1. Command-line arguments (`--channel-token` / `--channel-id` / `--secret` / `--private-key` / `--kid`)
2. Environment variables
3. Profile (`--profile <name>`, or the default profile)

### Environment variables

| Variable | Purpose |
|---|---|
| `LINE_CHANNEL_ACCESS_TOKEN` | Channel access token |
| `LINE_CHANNEL_ID` | Channel id |
| `LINE_CHANNEL_SECRET` | Channel secret (webhook signature verification, token revoke) |
| `LINE_PRIVATE_KEY_PATH` | Path to the RSA private key (PEM) for JWT assertions |
| `LINE_KID` | Key id of the signing key |
| `LINE_PROFILE` | Profile name to use |
| `LINE_CONFIG` | Override the config file path |

### Global options (all commands)

| Option | Description |
|---|---|
| `--profile <name>` | Profile to use |
| `--channel-token <t>` | Token override (wins over env/profile) |
| `--json` | Emit machine-readable JSON |
| `--verbose` | Show details on error |

---

## Command reference

### A. Token management — `line token`

```sh
# Issue (v2.1 by default; --kind stateless for a stateless token)
line token issue --kind v2.1 --days 30 \
  --channel-id 123 --kid KID --private-key ./key.pem

line token issue --store          # issue and store into the resolved profile
line token verify --token <t>     # check validity and remaining lifetime
line token revoke --token <t>     # revoke (requires --channel-id / --secret)
```

- Issuance requires the channel id, key id, and private key (PEM). The CLI builds and sends an RS256 JWT assertion.
- On the CLI the issued token is written to stdout and metadata to stderr (pipe-friendly). Use `--store` to save it into the resolved profile.

### B. Message send & bot lookup — `line message` / `line bot`

Message content is provided via one of `--text`, `--flex <file>`, or `--messages <file>`.

```sh
line message push --to <id> --text "Hello"
line message push --to <id> --flex ./flex.json --alt-text "Notice"
line message push --to <id> --messages ./messages.json     # a messages-array JSON verbatim
line message multicast --to id1,id2,id3 --text "To many"
line message broadcast --text "To everyone"
line message reply --reply-token <token> --text "Reply"
line message content <messageId> -o ./image.jpg           # download a message's binary content

line bot info
line bot quota                # send quota limit
line bot quota consumption    # this month's consumption
line bot profile <userId>
```

- `--flex` takes a Flex message `contents` JSON and automatically wraps it into a Flex message with `altText`.
- `--messages` sends a `[{ "type": "text", "text": "..." }, ...]` messages array as-is.
- `content` is fetched from the data host (`api-data.line.me`); the facade routes it automatically.

### C. Webhook development — `line webhook`

```sh
# Local receiver (verifies the signature and prints incoming events)
line webhook listen --port 5000

# Verify a stored payload's signature and summarize its events
line webhook verify --body ./payload.json --signature <x-line-signature>

# Replay a stored payload to a local app (no signature added; destination not validated)
line webhook replay --body ./payload.json --to http://localhost:5000/webhook

# Webhook endpoint configuration (channel access token; e.g. repoint at a fresh dev-tunnel URL)
line webhook get-endpoint                        # show the configured URL and active state
line webhook set-endpoint --url https://<tunnel>/callback
line webhook test-endpoint                       # ask LINE to probe the endpoint and report reachability
```

- Signature verification (`listen` / `verify`) requires the channel secret (profile / `--secret`); the endpoint commands (`get/set/test-endpoint`) use the channel access token instead.
- The URL for `set-endpoint`, `test-endpoint`, and `liff update-url` must be absolute **https**.
- Combine `listen` with an external tunnel (cloudflared / ngrok, etc.) to receive real webhooks from LINE. The tunnel itself is not bundled. `set-endpoint` then updates the LINE-side webhook URL without visiting the console.

### D. LIFF management — `line liff`

```sh
line liff list                                # lists liffId + URL — use it to find the id
line liff add --file ./app.json               # add from a LIFF app definition JSON
line liff update <liffId> --file ./app.json   # full update from a JSON definition
line liff update-url <liffId> --url https://<tunnel>/   # update only view.url (https), e.g. a fresh dev tunnel
line liff delete <liffId>
```

### E. Rich menu — `line richmenu`

```sh
line richmenu create --file ./richmenu.json    # create from a JSON definition; prints the new id
line richmenu validate --file ./richmenu.json   # validate a definition without creating it
line richmenu image <richMenuId> --file ./menu.png   # upload the image (PNG/JPEG; content type inferred)
line richmenu image-download <richMenuId> -o ./menu.png
line richmenu list
line richmenu get <richMenuId>
line richmenu delete <richMenuId>
line richmenu set-default <richMenuId>          # default for all users
line richmenu get-default
line richmenu cancel-default
line richmenu link <userId> <richMenuId>        # per-user link
line richmenu unlink <userId>
line richmenu id-of-user <userId>
```

- A typical dev cycle: `create` → `image` → `set-default` (or `link` to your own userId) → check on your device → iterate.
- Image upload requires PNG or JPEG; the content type is inferred from the file extension.

### F. Insight / Audience / Shop — `line insight` / `line audience` / `line shop`

```sh
# Insight (statistics; all read-only; dates are yyyyMMdd)
line insight demographic
line insight deliveries 20260715
line insight followers 20260715
line insight events <requestId>
line insight per-unit <unit> --from 20260701 --to 20260715
line insight richmenu-summary <richMenuId> --from 20260701 --to 20260715
line insight richmenu-daily <richMenuId> --from 20260701 --to 20260715

# Manage Audience
line audience list --page 1 --size 20
line audience get <audienceGroupId>
line audience create --file ./create-audience.json        # with initial user IDs
line audience add-users --file ./add-audience.json         # carries audienceGroupId
line audience delete <audienceGroupId>
line audience upload-file --file ./user-ids.txt --description "my audience"   # one ID/IFA per line
line audience add-file <audienceGroupId> --file ./more-ids.txt

# Shop
line shop mission --file ./mission.json
```

- `audience upload-file` / `add-file` take a text file (one user ID or IFA per line) and are **CLI-only** — binary/file input is impractical over MCP.

### Exit codes

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | General error |
| `2` | Argument error (invalid input, missing input file, etc.) |
| `3` | Authentication / credential error |
| `4` | LINE API error (HTTP 4xx/5xx) |

---

## Use as an MCP server

`line mcp` starts an MCP server over stdio so AI agents can operate LINE as tools.

```sh
line mcp                       # all tools enabled
line mcp --read-only           # expose read-only tools only
line mcp --allow-secret-output # allow token issue to return the raw token (withheld by default)
line mcp --allow-remote-replay # allow non-loopback destinations for webhook replay (loopback-only by default)
```

### Tool list

CLI commands are exposed as `line_<area>_<verb>` (except `webhook listen`).

- Read-only (available even under `--read-only`): `line_message_schema` / `line_richmenu_schema` / `line_richmenu_list` / `line_richmenu_get` / `line_richmenu_get_default` / `line_richmenu_id_of_user` / `line_insight_demographic` / `line_insight_deliveries` / `line_insight_followers` / `line_insight_events` / `line_insight_per_unit` / `line_insight_richmenu_summary` / `line_insight_richmenu_daily` / `line_audience_list` / `line_audience_get` / `line_bot_info` / `line_bot_quota` / `line_bot_quota_consumption` / `line_bot_profile` / `line_liff_list` / `line_token_verify` / `line_webhook_verify` / `line_webhook_get_endpoint` / `line_webhook_test_endpoint` / `line_ping`
- Mutating (excluded under `--read-only`): `line_message_push` / `line_message_multicast` / `line_message_broadcast` / `line_message_reply` / `line_richmenu_create` / `line_richmenu_delete` / `line_richmenu_set_default` / `line_richmenu_cancel_default` / `line_richmenu_link` / `line_richmenu_unlink` / `line_audience_create` / `line_audience_add_users` / `line_audience_delete` / `line_shop_mission` / `line_liff_add` / `line_liff_update` / `line_liff_update_url` / `line_liff_delete` / `line_token_issue` / `line_token_revoke` / `line_webhook_replay` / `line_webhook_set_endpoint`

> Audience by-file uploads (`upload-file` / `add-file`) are **CLI-only** — binary/file input is impractical over MCP, so `line_audience_create` directs you to the CLI for file uploads.

> **Rich menu dev cycle across MCP + CLI:** assemble the menu with the agent (`line_richmenu_schema` → build JSON → `line_richmenu_create` with `dryRun=true` to validate, then create), then upload the image with the **CLI** (`line richmenu image <id> --file menu.png`) — binary upload is impractical over MCP, so it is intentionally CLI-only — and finally `line_richmenu_set_default` / `line_richmenu_link` and check on your device.

Each tool accepts an optional `profile` argument; credentials are resolved from the profile.

### Building messages with an AI agent (flex / template)

A primary MCP use case is a **build → validate → send-and-see** loop: have the agent assemble a rich message, type-check it, then push it to your own device to check the appearance, and iterate. Two aids make this reliable:

- **`line_message_schema(type)`** returns the JSON Schema for LINE message objects so the agent can build a *shape-valid* `messagesJson`. `type` is one of `all` / `flex` / `template` / `imagemap` / `quickReply` / `action` (default `flex`). It is a read-only tool (available under `--read-only`) and returns no secrets. The schema is extracted from the same OpenAPI spec Kiota generates from, so it never drifts from the models; references are kept (`$ref` + `$defs`) rather than inlined because `FlexBox` is self-recursive.
  - Simple messages (text / image / video / audio / location / sticker) are trivial and shown inline in the send-tool descriptions — you usually only need the schema for **flex** or **template**.
- **`dryRun: true`** on the send tools (`line_message_push` / `multicast` / `broadcast` / `reply`) parses and shape-checks the messages and returns their parsed types **without sending** (no API call, no credentials required). Use it as a safety check before an actual send.

Typical flow: `line_message_schema type=flex` → build the Flex JSON → `line_message_push ... dryRun=true` (validate) → `line_message_push ...` (send to your own userId) → check on your device.

### Security design (MCP)

MCP tool results are assumed to enter the model's context (sent to the LLM provider, conversation history, logs), so the following protections are built in:

- **`line_token_issue` does not return the raw token by default.** The issued token is stored into the local profile and the result contains only metadata (`tokenType` / `expiresInSeconds` / `keyId` / `maskedToken` / `storedProfile`). Subsequent send tools operate via the stored profile. To obtain the raw token, start the server with `--allow-secret-output` and pass `reveal: true` in the tool call.
- **`line_webhook_replay` allows loopback destinations only by default** (SSRF mitigation). Remote destinations require `--allow-remote-replay`.
- Mutating / sending tools state their side effects in the description.

### Register with Claude Code

```sh
claude mcp add line -- line mcp
# read-only registration
claude mcp add line -- line mcp --read-only
```

### Register with Claude Desktop (`claude_desktop_config.json`)

```jsonc
{
  "mcpServers": {
    "line": {
      "command": "line",
      "args": ["mcp"]
    }
  }
}
```

---

## Build from source

```sh
dotnet build tools/Line.OpenApi.Tools/Line.OpenApi.Tools.csproj
dotnet test  tests/Line.OpenApi.Tools.Tests/Line.OpenApi.Tools.Tests.csproj
```

## Related documentation

- Specification: [`docs/CLI-MCP-tool-spec.md`](https://github.com/pierre3/line-openapi-dotnet/blob/main/docs/CLI-MCP-tool-spec.md)
- Core libraries: repository root [`README.md`](https://github.com/pierre3/line-openapi-dotnet/blob/main/README.md)

## License

MIT (see [`LICENSE`](https://github.com/pierre3/line-openapi-dotnet/blob/main/LICENSE) at the repository root).
