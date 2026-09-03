# LINE Flex Message Viewer

*Read this in [日本語](./README.ja.md).*

Build [LINE Flex Messages](https://developers.line.biz/en/docs/messaging-api/using-flex-messages/)
and see exactly how they'll look — a live, LINE-style preview that updates as you (or an
AI agent) edit the JSON. Flex Message JSON is verbose and hard to picture in your head;
this tool renders it the way the LINE app would, so you can get the layout and colors right
before you send anything.

There's no LINE account, API key, or network access involved. The preview runs entirely on
your own machine.

## What you can do

- **See your Flex Message rendered like LINE** — bubbles, carousels, images, buttons, and
  the usual layout/style properties, on a chat-style background with light/dark toggle.
- **Edit and watch it update live** — tweak the JSON in the editor and the preview re-renders
  instantly. Adjust spacing, colors, and text until it looks right.
- **Work together with an AI agent** — an agent (for example, the
  [`Line.OpenApi.Tools`](https://github.com/pierre3/line-openapi-dotnet) MCP server) builds the
  JSON, you fine-tune it by hand, and the agent reads your changes back to keep iterating.

## Three ways to use it

| | Best for |
| --- | --- |
| **Copilot canvas** | Previewing inside the Copilot App's side panel while an agent builds the message. |
| **MCP server** | Claude Desktop / Claude Code (or any MCP client) — the preview opens in your browser. |
| **Standalone page** | A quick look with no app and no setup — just open an HTML file in your browser. |

All three share the same renderer, so a message looks the same everywhere.

## Install

This is a public repository, so the simplest way to add the canvas extension is straight from
the folder URL:

```
install_extension https://github.com/pierre3/line-openapi-dotnet/tree/main/extensions/line-flex-viewer
```

That's all you need for the Copilot canvas. The MCP server and the standalone page are
described below and need no install step of their own.

## Using it

### In the Copilot App (canvas)

Ask your agent to preview a Flex Message. It opens the canvas and pushes the JSON; the side
panel renders it immediately. From there:

- Edit the JSON on the left — the preview on the right updates as you type (`Ctrl/Cmd+Enter`
  forces a re-render).
- Use the toolbar to **Format**, **Copy JSON**, **Load sample**, or **toggle the background**.
- Your edits are saved automatically and restored when you reopen the same document.

Behind the scenes the agent uses three actions: `set_content` (show/replace the JSON),
`get_content` (read back your edits), and `validate` (a quick structural check).

### In Claude Desktop / Claude Code (MCP)

Claude extends through MCP servers rather than canvases, so this extension bundles a small,
zero-dependency MCP server (`mcp/server.mjs`). When the AI previews a message, the server
starts a local preview and opens it in your default browser; later changes stream in live.

Register it (needs Node.js 18+ — no `npm install`). Replace `<REPO>` with where you cloned
this repository:

**Claude Desktop** — add to `claude_desktop_config.json` (macOS:
`~/Library/Application Support/Claude/`, Windows: `%APPDATA%\Claude\`):

```jsonc
{
  "mcpServers": {
    "line-flex-viewer": {
      "command": "node",
      "args": ["<REPO>/extensions/line-flex-viewer/mcp/server.mjs"]
    }
  }
}
```

**Claude Code**:

```bash
claude mcp add line-flex-viewer -- node <REPO>/extensions/line-flex-viewer/mcp/server.mjs
```

Then ask Claude to "preview this Flex Message." The tools it can call:

| Tool | What it does |
| --- | --- |
| `preview_flex_message` | Render Flex JSON and open/live-update the browser preview. |
| `get_flex_content` | Read the JSON currently shown, **including your in-browser edits**. |
| `validate_flex_message` | Check the JSON's structure without opening a browser. |
| `open_preview` | Reopen the preview tab (e.g. after you closed it). |

Optional settings:

| Variable | Default | Purpose |
| --- | --- | --- |
| `LINE_FLEX_MCP_NO_OPEN` | (unset) | Don't auto-open the browser; the URL is still returned. |
| `LINE_FLEX_MCP_STATE_DIR` | OS temp dir | Where the current preview content is saved. |
| `LINE_FLEX_MCP_HTML` | `viewer.html` | Which page to serve (set to `standalone.html` for the client-side viewer). |

### On its own (standalone page)

Want a quick preview with no app at all? Open `web/standalone.html` in your browser. It runs
100% in the browser — no server, no agent:

- Double-click the file (works over `file://`), or serve the folder if your browser is strict
  about local files:

  ```bash
  cd extensions/line-flex-viewer/web
  python -m http.server 8791     # → http://127.0.0.1:8791/standalone.html
  ```

- Besides live editing and the toolbar, it can **open/download** JSON files and create a
  **share link** — the whole message is encoded into the URL (`#json=...`), so anyone who
  opens the link sees the same preview. Your last edit is remembered in the browser.

## What the preview supports

Containers `bubble` (nano–giga) and `carousel`; the `header` / `hero` / `body` / `footer`
blocks with their `styles`; and components `box` (horizontal / vertical / baseline), `text`,
`span`, `image`, `button`, `icon`, `separator`, `filler`, and `video`. Most layout and style
properties are honored — `flex`, `spacing`, `margin`, padding, borders, `cornerRadius`,
`justifyContent`, `alignItems`, `position` / `offset`, `gravity`, `align`, `wrap`, `maxLines`,
`aspectRatio` / `aspectMode`, and so on.

> The preview is a **CSS approximation** of LINE's renderer. Sizes follow LINE's documented
> scale, but exact pixels may differ slightly from the LINE app. Use it to get the design
> right, then confirm the final look on a real device.

## Related

Prefer to drive the preview from the `line` command-line / MCP tool instead of this extension?
The same renderer ships in [`Line.OpenApi.Tools`](https://github.com/pierre3/line-openapi-dotnet)
as the `line_flex_*` tools.

## Sharing as a gist (optional)

Because the folder includes `copilot-extension.json`, you can also share it as a private gist
("Share extension as gist…" / `share_extension`) and install it elsewhere with `install_extension`.
For a public repo, though, the folder-URL install above is simpler.
