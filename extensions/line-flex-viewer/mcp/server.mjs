#!/usr/bin/env node
/*
 * LINE Flex Message Viewer — MCP server (stdio, zero-dependency).
 *
 * Lets any MCP client (Claude Desktop, Claude Code, etc.) preview LINE Flex
 * Message JSON in a live browser tab. The AI builds Flex JSON and calls
 * `preview_flex_message`; this server serves the shared renderer over a local
 * loopback HTTP port, opens it in the default browser, and live-updates it via
 * SSE. `get_flex_content` reads back the user's in-browser edits.
 *
 * Uses only Node.js built-ins — run with `node server.mjs`, no `npm install`.
 * Reuses the same web assets as the Copilot canvas (../web).
 */

import { createServer } from "node:http";
import { readFile, mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { dirname, join, extname, resolve as resolvePath } from "node:path";
import { tmpdir } from "node:os";
import { spawn } from "node:child_process";
import { resolveAssetDir, resolveMediaRequest } from "../lib/assets.mjs";

const __dirname = dirname(fileURLToPath(import.meta.url));
const WEB_DIR = join(__dirname, "..", "web");

const STATE_DIR = process.env.LINE_FLEX_MCP_STATE_DIR || join(tmpdir(), "line-flex-mcp");
const STATE_FILE = join(STATE_DIR, "content.json");
const HTML_ENTRY = process.env.LINE_FLEX_MCP_HTML || "viewer.html"; // viewer.html = live push
const AUTO_OPEN = process.env.LINE_FLEX_MCP_NO_OPEN ? false : true;

// Opt-in local media serving: when LINE_FLEX_MCP_ASSET_DIR is set, media files under it
// are served so a Flex message can reference local artwork/video by a relative url.
// Disabled (null) unless configured. Mirrors the .NET FlexPreviewService.
const ASSET_DIR = resolveAssetDir(process.env.LINE_FLEX_MCP_ASSET_DIR);

const STATIC_FILES = new Set([
  "viewer.html", "viewer.js", "renderer.js", "flex.css", "samples.js",
  "standalone.html", "standalone.js",
]);
const CONTENT_TYPES = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".json": "application/json; charset=utf-8",
};

const state = {
  server: null,
  url: null,
  port: null,
  content: null,
  clients: new Set(),
  opened: false,
};

function log(...args) {
  // stdout is reserved for JSON-RPC; everything human-facing goes to stderr.
  process.stderr.write("[line-flex-mcp] " + args.map(String).join(" ") + "\n");
}

// --- persistence ---------------------------------------------------------

async function loadContent() {
  try {
    const raw = await readFile(STATE_FILE, "utf8");
    const parsed = JSON.parse(raw);
    return parsed && typeof parsed === "object" && "content" in parsed ? parsed.content : parsed;
  } catch {
    return null;
  }
}

async function saveContent(content) {
  await mkdir(STATE_DIR, { recursive: true });
  await writeFile(STATE_FILE, JSON.stringify({ content }, null, 2), "utf8");
}

// Accept a message wrapper, a bare container, or a JSON string; return an object.
function normalizeContent(input) {
  let value = input;
  if (typeof value === "string") value = JSON.parse(value);
  if (!value || typeof value !== "object") {
    throw new Error("content must be an object or JSON string");
  }
  return value;
}

// --- structural validation ------------------------------------------------

function extractContainer(json) {
  if (!json || typeof json !== "object") return null;
  if (json.type === "flex" && json.contents && typeof json.contents === "object") return json.contents;
  if (json.type === "bubble" || json.type === "carousel") return json;
  if (json.contents && (json.contents.type === "bubble" || json.contents.type === "carousel")) {
    return json.contents;
  }
  return null;
}

function validateContent(json) {
  const warnings = [];
  const container = extractContainer(json);
  if (!container) {
    warnings.push('root must be a "bubble"/"carousel" container or a type:"flex" message');
    return { valid: false, warnings };
  }
  let bubbles = [];
  if (container.type === "carousel") {
    if (!Array.isArray(container.contents) || container.contents.length === 0) {
      warnings.push("carousel.contents is empty");
    } else {
      if (container.contents.length > 12) warnings.push("carousel supports at most 12 bubbles");
      bubbles = container.contents;
    }
  } else {
    bubbles = [container];
  }
  bubbles.forEach((b, i) => {
    if (!b || b.type !== "bubble") {
      warnings.push(`contents[${i}] is not a bubble`);
      return;
    }
    if (!b.header && !b.hero && !b.body && !b.footer) {
      warnings.push(`bubble[${i}] has no header/hero/body/footer block`);
    }
  });
  return { valid: warnings.length === 0, warnings };
}

// --- SSE broadcast --------------------------------------------------------

function broadcast() {
  const payload = `event: content\ndata: ${JSON.stringify({ content: state.content })}\n\n`;
  for (const res of state.clients) {
    try {
      res.write(payload);
    } catch {
      /* ignore broken pipe */
    }
  }
}

// --- HTTP server ----------------------------------------------------------

function renderIndex(html) {
  const cfg = { docId: "default", instanceId: "mcp" };
  const script = `<script>window.__FLEX_CFG__=${JSON.stringify(cfg)};</script>`;
  return html.replace(
    '<script src="./samples.js"></script>',
    script + '\n    <script src="./samples.js"></script>'
  );
}

function sendFile(res, filePath, contentType) {
  readFile(filePath)
    .then((buf) => {
      res.writeHead(200, { "Content-Type": contentType });
      res.end(buf);
    })
    .catch(() => {
      res.writeHead(404);
      res.end("not found");
    });
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    let size = 0;
    req.on("data", (c) => {
      size += c.length;
      if (size > 5 * 1024 * 1024) {
        reject(new Error("payload too large"));
        req.destroy();
        return;
      }
      chunks.push(c);
    });
    req.on("end", () => resolve(Buffer.concat(chunks).toString("utf8")));
    req.on("error", reject);
  });
}

function handleRequest(req, res) {
  const url = new URL(req.url, "http://127.0.0.1");
  const path = url.pathname;

  if (req.method === "GET" && (path === "/" || path === "/index.html")) {
    readFile(join(WEB_DIR, HTML_ENTRY), "utf8")
      .then((html) => {
        res.writeHead(200, { "Content-Type": CONTENT_TYPES[".html"] });
        res.end(renderIndex(html));
      })
      .catch(() => {
        res.writeHead(500);
        res.end("template error");
      });
    return;
  }

  if (req.method === "GET" && !path.startsWith("/api/")) {
    const name = path.replace(/^\//, "");
    if (STATIC_FILES.has(name)) {
      const filePath = resolvePath(WEB_DIR, name);
      if (filePath.startsWith(resolvePath(WEB_DIR))) {
        sendFile(res, filePath, CONTENT_TYPES[extname(name)] || "application/octet-stream");
        return;
      }
    }
    // Local media (opt-in via LINE_FLEX_MCP_ASSET_DIR) so a Flex message can reference
    // artwork/video by a relative url. The helper applies the loopback-host guard and
    // path confinement; it returns null (→ 404) when serving is disabled or refused.
    const media = resolveMediaRequest({ path, host: req.headers.host, boundPort: state.port, assetDir: ASSET_DIR });
    if (media) {
      sendFile(res, media.file, media.contentType);
      return;
    }
    res.writeHead(404);
    res.end("not found");
    return;
  }

  if (req.method === "GET" && path === "/api/state") {
    res.writeHead(200, { "Content-Type": CONTENT_TYPES[".json"] });
    res.end(JSON.stringify({ docId: "default", content: state.content ?? null }));
    return;
  }

  if (req.method === "POST" && path === "/api/state") {
    readBody(req)
      .then(async (body) => {
        const data = JSON.parse(body || "{}");
        state.content = data.content ?? null;
        await saveContent(state.content);
        res.writeHead(200, { "Content-Type": CONTENT_TYPES[".json"] });
        res.end(JSON.stringify({ ok: true }));
      })
      .catch((e) => {
        res.writeHead(400, { "Content-Type": CONTENT_TYPES[".json"] });
        res.end(JSON.stringify({ ok: false, error: String(e && e.message) }));
      });
    return;
  }

  if (req.method === "GET" && path === "/api/events") {
    res.writeHead(200, {
      "Content-Type": "text/event-stream",
      "Cache-Control": "no-cache",
      Connection: "keep-alive",
    });
    res.write(": connected\n\n");
    state.clients.add(res);
    const keepAlive = setInterval(() => {
      try {
        res.write(": ping\n\n");
      } catch {
        /* ignore */
      }
    }, 25000);
    req.on("close", () => {
      clearInterval(keepAlive);
      state.clients.delete(res);
    });
    return;
  }

  res.writeHead(404);
  res.end("not found");
}

async function ensureServer() {
  if (state.server) return state.url;
  if (state.content == null) state.content = await loadContent();
  const server = createServer(handleRequest);
  await new Promise((r) => server.listen(0, "127.0.0.1", r));
  const addr = server.address();
  const port = typeof addr === "object" && addr ? addr.port : 0;
  state.server = server;
  state.port = port;
  state.url = `http://127.0.0.1:${port}/`;
  log("preview server listening at", state.url);
  return state.url;
}

function openBrowser(url) {
  try {
    let cmd, args;
    if (process.platform === "win32") {
      cmd = "cmd";
      args = ["/c", "start", "", url];
    } else if (process.platform === "darwin") {
      cmd = "open";
      args = [url];
    } else {
      cmd = "xdg-open";
      args = [url];
    }
    const child = spawn(cmd, args, { detached: true, stdio: "ignore" });
    child.on("error", (e) => log("failed to open browser:", e.message));
    child.unref();
  } catch (e) {
    log("failed to open browser:", e && e.message);
  }
}

// --- MCP tools ------------------------------------------------------------

const TOOLS = [
  {
    name: "preview_flex_message",
    description:
      "Preview LINE Flex Message JSON in a live browser tab. Accepts a full flex message " +
      '({type:"flex",altText,contents}), a bare bubble/carousel container, or a JSON string. ' +
      "Opens the preview in the default browser on first use and live-updates it on subsequent calls. " +
      "Returns the preview URL plus validation warnings.",
    inputSchema: {
      type: "object",
      properties: {
        content: {
          description: "Flex Message JSON: object (flex message / bubble / carousel) or JSON string.",
          type: ["object", "string"],
        },
        altText: { type: "string", description: "Optional alt text (metadata only)." },
      },
      required: ["content"],
    },
  },
  {
    name: "get_flex_content",
    description:
      "Return the Flex Message JSON currently shown in the preview, INCLUDING any edits the user " +
      "made in the browser editor. Use this to read back the user's tweaks.",
    inputSchema: { type: "object", properties: {} },
  },
  {
    name: "validate_flex_message",
    description:
      "Structurally validate Flex Message JSON (container/bubble/blocks) without opening a browser. " +
      "Returns { valid, warnings }.",
    inputSchema: {
      type: "object",
      properties: {
        content: {
          description: "Flex Message JSON to validate. Omit to validate the current preview content.",
          type: ["object", "string"],
        },
      },
    },
  },
  {
    name: "open_preview",
    description:
      "Open (or reopen) the live preview tab in the default browser and return its URL, without changing content.",
    inputSchema: { type: "object", properties: {} },
  },
];

function toolResultText(obj) {
  return { content: [{ type: "text", text: JSON.stringify(obj, null, 2) }] };
}

async function callTool(name, args) {
  args = args || {};
  if (name === "preview_flex_message") {
    const normalized = normalizeContent(args.content);
    const { valid, warnings } = validateContent(normalized);
    state.content = normalized;
    await saveContent(normalized);
    const url = await ensureServer();
    broadcast();
    if (AUTO_OPEN && !state.opened) {
      state.opened = true;
      openBrowser(url);
    }
    return toolResultText({ ok: true, url, valid, warnings, opened: state.opened });
  }
  if (name === "get_flex_content") {
    if (state.content == null) state.content = await loadContent();
    return toolResultText({ content: state.content ?? null });
  }
  if (name === "validate_flex_message") {
    let target = state.content;
    if (args.content !== undefined) target = normalizeContent(args.content);
    if (target == null) return toolResultText({ valid: false, warnings: ["no content to validate"] });
    return toolResultText(validateContent(target));
  }
  if (name === "open_preview") {
    const url = await ensureServer();
    state.opened = true;
    openBrowser(url);
    return toolResultText({ ok: true, url });
  }
  throw new Error(`unknown tool: ${name}`);
}

// --- MCP stdio JSON-RPC ---------------------------------------------------

const PROTOCOL_VERSION = "2024-11-05";
const SERVER_INFO = { name: "line-flex-viewer", version: "1.0.0" };

function send(msg) {
  process.stdout.write(JSON.stringify(msg) + "\n");
}

function reply(id, result) {
  send({ jsonrpc: "2.0", id, result });
}

function replyError(id, code, message) {
  send({ jsonrpc: "2.0", id, error: { code, message } });
}

async function handleMessage(msg) {
  if (!msg || msg.jsonrpc !== "2.0") return;
  const { id, method, params } = msg;

  // Notifications (no id) — nothing to reply.
  if (id === undefined || id === null) {
    // e.g. notifications/initialized, notifications/cancelled
    return;
  }

  try {
    if (method === "initialize") {
      const clientProto = params && params.protocolVersion;
      reply(id, {
        protocolVersion: typeof clientProto === "string" ? clientProto : PROTOCOL_VERSION,
        capabilities: { tools: { listChanged: false } },
        serverInfo: SERVER_INFO,
      });
      return;
    }
    if (method === "ping") {
      reply(id, {});
      return;
    }
    if (method === "tools/list") {
      reply(id, { tools: TOOLS });
      return;
    }
    if (method === "tools/call") {
      const name = params && params.name;
      const args = params && params.arguments;
      try {
        const result = await callTool(name, args);
        reply(id, result);
      } catch (e) {
        // Tool-level errors are reported as a result with isError, per MCP.
        reply(id, {
          content: [{ type: "text", text: `Error: ${e && e.message ? e.message : String(e)}` }],
          isError: true,
        });
      }
      return;
    }
    replyError(id, -32601, `Method not found: ${method}`);
  } catch (e) {
    replyError(id, -32603, e && e.message ? e.message : String(e));
  }
}

function main() {
  let buffer = "";
  process.stdin.setEncoding("utf8");
  process.stdin.on("data", (chunk) => {
    buffer += chunk;
    let idx;
    while ((idx = buffer.indexOf("\n")) >= 0) {
      const line = buffer.slice(0, idx).trim();
      buffer = buffer.slice(idx + 1);
      if (!line) continue;
      let msg;
      try {
        msg = JSON.parse(line);
      } catch {
        log("failed to parse message:", line.slice(0, 200));
        continue;
      }
      handleMessage(msg);
    }
  });
  process.stdin.on("end", () => process.exit(0));
  log("ready (stdio). Web assets:", WEB_DIR);
}

main();
