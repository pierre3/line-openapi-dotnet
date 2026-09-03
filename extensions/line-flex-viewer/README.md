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
- **Work together with an AI agent** — an agent builds the JSON, you fine-tune it by hand,
  and the agent reads your changes back to keep iterating.

## Two main ways to use it

The preview is used in two main scenarios. Both share the same renderer, so a message looks
the same in either one.

### 1. The Copilot App canvas

Add the canvas extension straight from this repo's folder URL:

```
install_extension https://github.com/pierre3/line-openapi-dotnet/tree/main/extensions/line-flex-viewer
```

Then ask your agent to preview a Flex Message. It opens the canvas and pushes the JSON, and
the side panel renders it immediately. From there:

- Edit the JSON on the left — the preview on the right updates as you type (`Ctrl/Cmd+Enter`
  forces a re-render).
- Use the toolbar to **Format**, **Copy JSON**, **Load sample**, or **toggle the background**.
- Your edits are saved automatically and restored when you reopen the same document.

Behind the scenes the agent uses three actions: `set_content` (show/replace the JSON),
`get_content` (read back your edits), and `validate` (a quick structural check).

### 2. The `line` MCP tool (Line.OpenApi.Tools)

If you're already using the [`Line.OpenApi.Tools`](https://github.com/pierre3/line-openapi-dotnet/tree/main/tools/Line.OpenApi.Tools)
`line` command-line / MCP tool, the same renderer is built in as the `line_flex_*` tools —
no separate install. Your AI agent calls `line_flex_preview` and the preview opens in your
browser, updating live as you iterate; `line_flex_get_content` reads your in-browser edits
back. See that tool's README for details. This is the recommended path for Claude Desktop /
Claude Code and other MCP clients.

## Alternative: the bundled Node MCP server

If you want an MCP preview but aren't using the .NET `line` tool, this folder also ships a
small, zero-dependency MCP server (`mcp/server.mjs`) that reuses the same renderer. It's a
self-contained alternative to option 2 above — handy when you'd rather not install the .NET tool.

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

Optional settings: `LINE_FLEX_MCP_NO_OPEN` (don't auto-open the browser; the URL is still
returned) and `LINE_FLEX_MCP_STATE_DIR` (where the current preview content is saved).

## What the preview supports

| Category | Supported |
| --- | --- |
| **Containers** | `bubble` (sizes `nano`–`giga`), `carousel` |
| **Blocks** | `header`, `hero`, `body`, `footer` — including their `styles` |
| **Components** | `box` (`horizontal` / `vertical` / `baseline`), `text`, `span`, `image`, `button`, `icon`, `separator`, `filler`, `video` |
| **Layout & style props** | `flex`, `spacing`, `margin`, padding, borders, `cornerRadius`, `justifyContent`, `alignItems`, `position` / `offset`, `gravity`, `align`, `wrap`, `maxLines`, `aspectRatio` / `aspectMode`, and more |

> The preview is a **CSS approximation** of LINE's renderer. Sizes follow LINE's documented
> scale, but exact pixels may differ slightly from the LINE app. Use it to get the design
> right, then confirm the final look on a real device.
