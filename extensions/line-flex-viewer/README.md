# LINE Flex Message Viewer (Canvas Extension)

*Read this in [日本語](./README.ja.md).*

A Copilot CLI **canvas extension** that previews and lets you tweak
[LINE Flex Message](https://developers.line.biz/en/docs/messaging-api/using-flex-messages/)
JSON with a live, LINE-style render in the app's side panel.

The same renderer is reused three ways:

- **Canvas extension** — live preview inside the Copilot App side panel.
- **Standalone page** — a 100% client-side browser preview, no Copilot App required.
- **MCP server** — a live browser preview for any MCP client (e.g. Claude Desktop / Claude Code).

## Intended workflow

1. An AI/agent builds Flex Message JSON — e.g. via the
   [`Line.OpenApi.Tools`](https://github.com/pierre3/line-openapi-dotnet) MCP server.
2. The agent pushes it to the canvas with the **`set_content`** action; the panel
   renders it instantly.
3. You tweak the JSON directly in the panel editor (live re-render + auto-save).
4. The agent reads your edits back with **`get_content`** and continues iterating.
5. Publish/share the extension via a private gist (see below).

## Canvas actions (agent-facing)

| Action        | Purpose |
| ------------- | ------- |
| `set_content` | Set the Flex JSON shown in the canvas and re-render. Accepts a full flex message (`{type:"flex",altText,contents}`), a bare `bubble`/`carousel` container, or a JSON string. Returns `{ ok, valid, warnings }`. |
| `get_content` | Return the JSON currently in the canvas, **including the user's panel edits**. |
| `validate`    | Lightweight structural check of the current (or supplied) JSON. |

### Open input

```jsonc
{
  "docId": "my-doc",        // optional: stable id; reopening restores content
  "content": { /* ... */ }, // optional: initial Flex JSON
  "altText": "..."          // optional
}
```

## Panel UI

- **Left**: JSON editor (live re-render, `Ctrl/Cmd+Enter` to force render, `Tab` inserts spaces).
- **Right**: LINE-style preview on a chat background (toggle light/dark).
- Toolbar: **Render** / **Format** / **Copy JSON** / **Load sample** / **Toggle background**.

## Rendering support

Containers `bubble` (nano–giga) and `carousel`; blocks `header`/`hero`/`body`/`footer`
with `styles`; components `box` (horizontal/vertical/baseline), `text`, `span`,
`image`, `button`, `icon`, `separator`, `filler`, and `video` (preview). Most
layout/style properties are honored (`flex`, `spacing`, `margin`, padding, borders,
`cornerRadius`, `justifyContent`, `alignItems`, `position`/`offset`, `gravity`,
`align`, `wrap`, `maxLines`, `aspectRatio`/`aspectMode`, etc.).

> The preview is a **CSS approximation** of LINE's renderer. Keyword→px sizes follow
> LINE's documented scale but exact pixel metrics may differ slightly from the LINE app.

## State / storage

Content persists under `$COPILOT_HOME/extensions/line-flex-viewer/artifacts/<docId>.json`
(never inside the repo), keyed by `docId` so reopening restores it.

## Browser preview without the Copilot App (standalone)

The canvas preview is really a **local web app** — the extension spins up a
loopback HTTP server (`127.0.0.1:<random-port>`) per open panel while the Copilot
App is running. To preview **without the Copilot App**, use the standalone page,
which runs 100% client-side (no server, no agent) and reuses the same renderer:

- **Open directly**: double-click `web/standalone.html` (works over `file://`).
- **Or serve statically** (recommended; some browsers restrict `file://`):

  ```bash
  cd .github/extensions/line-flex-viewer/web
  python -m http.server 8791          # → http://127.0.0.1:8791/standalone.html
  # or: npx serve .
  ```

Standalone features:

- Live editor + LINE-style preview, **Format** / **Copy JSON** / **Load sample** / **Toggle background**.
- **Open file / Download** — import/export Flex JSON as a `.json` file.
- **Share link** — encodes the current JSON into the URL (`#json=<base64>`) and
  copies it to the clipboard, so you can share a preview link with anyone (no app,
  no server persistence — the content lives in the link itself).
- Auto-saves to `localStorage`, so your last edit is restored on reload.

Seed priority on load: URL `#json=` hash > `localStorage` > first sample.

While the Copilot App *is* running, the same standalone page is also reachable at
`http://127.0.0.1:<panel-port>/standalone.html` (the panel URL with `/standalone.html`).

## Use from Claude (Desktop / Code) or any MCP client

Claude Desktop / Claude Code extend via **MCP servers**, not canvases. This repo
bundles an MCP server (`mcp/server.mjs`, **Node built-ins only — zero dependencies**)
that reuses the same renderer. When the AI builds Flex JSON and calls
`preview_flex_message`, the server starts a local preview server and opens a
**live preview in your default browser** (subsequent updates stream in over SSE).

### MCP tools

| Tool | Description |
| ---- | ---- |
| `preview_flex_message` | Preview Flex JSON (flex message / bubble / carousel / JSON string) in the browser. Opens the browser on first use, then live-updates. Returns `{ url, valid, warnings }`. |
| `get_flex_content` | Return the JSON currently in the preview (**including the user's browser edits**). |
| `validate_flex_message` | Structurally validate JSON (no browser needed). Returns `{ valid, warnings }`. |
| `open_preview` | Open/reopen the preview tab and return its URL, without changing content. |

### Setup

You only need Node.js 18+ (`npm install` is not required). Replace `<REPO>` with the
absolute path where you placed this repository.

**Claude Desktop** — add to `claude_desktop_config.json` (macOS:
`~/Library/Application Support/Claude/`, Windows: `%APPDATA%\Claude\`):

```jsonc
{
  "mcpServers": {
    "line-flex-viewer": {
      "command": "node",
      "args": ["<REPO>/.github/extensions/line-flex-viewer/mcp/server.mjs"]
    }
  }
}
```

**Claude Code** — register via the CLI:

```bash
claude mcp add line-flex-viewer -- node <REPO>/.github/extensions/line-flex-viewer/mcp/server.mjs
```

Once registered, ask Claude to "preview this Flex Message" and `preview_flex_message`
runs, showing the preview in your browser. Tweak the JSON in the browser and Claude
can read your edits back with `get_flex_content`.

### Environment variables (optional)

| Variable | Default | Purpose |
| ---- | ---- | ---- |
| `LINE_FLEX_MCP_NO_OPEN` | (unset) | Set to disable auto-opening the browser (the URL is still returned). |
| `LINE_FLEX_MCP_STATE_DIR` | OS temp dir | Where preview content is persisted. |
| `LINE_FLEX_MCP_HTML` | `viewer.html` | Which page to serve (can be changed to `standalone.html`). |

> **Not interested in MCP?** The standalone browser preview above works entirely on
> its own. The MCP server is for when you want to automate the "AI ↔ preview" loop.

## Publish / share

This folder includes `copilot-extension.json`, so it can be shared as a private gist
from the command palette ("Share extension as gist…") or the `share_extension` tool,
and installed elsewhere with "Install extension from gist…" / `install_extension`.
