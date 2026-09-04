// Extension: line-flex-viewer
// LINE Flex Message previewer/editor canvas.
//
// Flow: an agent (e.g. via the Line.OpenApi.Tools MCP server) builds Flex
// Message JSON and pushes it to the canvas with the `set_content` action; the
// user previews and tweaks it in the side panel; the agent reads back the
// user's edits with `get_content`.
//
// Wiring only. The renderer, UI, and samples live under ./web and are served
// by a per-instance loopback HTTP server.

import { createServer } from "node:http";
import { readFile, mkdir, writeFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { dirname, join, extname, resolve as resolvePath } from "node:path";
import { homedir } from "node:os";
import { joinSession, createCanvas, CanvasError } from "@github/copilot-sdk/extension";
import { resolveAssetDir, resolveMediaRequest } from "./lib/assets.mjs";

const __dirname = dirname(fileURLToPath(import.meta.url));
const WEB_DIR = join(__dirname, "web");

// Durable artifact storage lives under COPILOT_HOME, never inside the repo.
const COPILOT_HOME = process.env.COPILOT_HOME || join(homedir(), ".copilot");
const ARTIFACT_DIR = join(COPILOT_HOME, "extensions", "line-flex-viewer", "artifacts");

// Opt-in local media serving: when LINE_FLEX_MCP_ASSET_DIR is set, media files under it
// are served so a Flex message can reference local artwork/video by a relative url.
// Disabled (null) unless configured. Mirrors the .NET FlexPreviewService.
const ASSET_DIR = resolveAssetDir(process.env.LINE_FLEX_MCP_ASSET_DIR);

const STATIC_FILES = new Set(["viewer.html", "viewer.js", "renderer.js", "flex.css", "samples.js", "standalone.html", "standalone.js"]);
const CONTENT_TYPES = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".json": "application/json; charset=utf-8",
};

// instanceId -> { server, url, docId, content, clients:Set<res>, instanceId }
const instances = new Map();

let logFn = () => {};

// --- persistence ---------------------------------------------------------

function safeDocId(docId) {
  const s = String(docId || "default").replace(/[^A-Za-z0-9._-]/g, "_");
  return s.slice(0, 120) || "default";
}

function artifactPath(docId) {
  return join(ARTIFACT_DIR, safeDocId(docId) + ".json");
}

async function loadContent(docId) {
  try {
    const raw = await readFile(artifactPath(docId), "utf8");
    const parsed = JSON.parse(raw);
    return parsed && typeof parsed === "object" && "content" in parsed ? parsed.content : parsed;
  } catch {
    return null;
  }
}

async function saveContent(docId, content) {
  await mkdir(ARTIFACT_DIR, { recursive: true });
  await writeFile(artifactPath(docId), JSON.stringify({ content }, null, 2), "utf8");
}

// Accept a message wrapper, a bare container, or a JSON string; return an object.
function normalizeContent(input) {
  let value = input;
  if (typeof value === "string") {
    value = JSON.parse(value);
  }
  if (!value || typeof value !== "object") {
    throw new CanvasError("invalid_content", "content must be an object or JSON string");
  }
  return value;
}

// --- structural validation (lightweight) ---------------------------------

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

// --- SSE broadcast -------------------------------------------------------

function broadcast(entry) {
  const payload = `event: content\ndata: ${JSON.stringify({ content: entry.content })}\n\n`;
  for (const res of entry.clients) {
    try {
      res.write(payload);
    } catch {
      /* ignore broken pipe */
    }
  }
}

// --- HTTP server ---------------------------------------------------------

function renderIndex(html, entry) {
  const cfg = { docId: entry.docId, instanceId: entry.instanceId };
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

function handleRequest(entry, req, res) {
  const url = new URL(req.url, "http://127.0.0.1");
  const path = url.pathname;

  // Index (with injected config)
  if (req.method === "GET" && (path === "/" || path === "/index.html")) {
    readFile(join(WEB_DIR, "viewer.html"), "utf8")
      .then((html) => {
        res.writeHead(200, { "Content-Type": CONTENT_TYPES[".html"] });
        res.end(renderIndex(html, entry));
      })
      .catch(() => {
        res.writeHead(500);
        res.end("template error");
      });
    return;
  }

  // Static assets (whitelisted)
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
    const media = resolveMediaRequest({ path, host: req.headers.host, boundPort: entry.port, assetDir: ASSET_DIR });
    if (media) {
      sendFile(res, media.file, media.contentType);
      return;
    }
    res.writeHead(404);
    res.end("not found");
    return;
  }

  // GET current state
  if (req.method === "GET" && path === "/api/state") {
    res.writeHead(200, { "Content-Type": CONTENT_TYPES[".json"] });
    res.end(JSON.stringify({ docId: entry.docId, content: entry.content ?? null }));
    return;
  }

  // POST state (from the iframe editor)
  if (req.method === "POST" && path === "/api/state") {
    readBody(req)
      .then(async (body) => {
        const data = JSON.parse(body || "{}");
        entry.content = data.content ?? null;
        await saveContent(entry.docId, entry.content);
        res.writeHead(200, { "Content-Type": CONTENT_TYPES[".json"] });
        res.end(JSON.stringify({ ok: true }));
      })
      .catch((e) => {
        res.writeHead(400, { "Content-Type": CONTENT_TYPES[".json"] });
        res.end(JSON.stringify({ ok: false, error: String(e && e.message) }));
      });
    return;
  }

  // SSE stream
  if (req.method === "GET" && path === "/api/events") {
    res.writeHead(200, {
      "Content-Type": "text/event-stream",
      "Cache-Control": "no-cache",
      Connection: "keep-alive",
    });
    res.write(": connected\n\n");
    entry.clients.add(res);
    const keepAlive = setInterval(() => {
      try {
        res.write(": ping\n\n");
      } catch {
        /* ignore */
      }
    }, 25000);
    req.on("close", () => {
      clearInterval(keepAlive);
      entry.clients.delete(res);
    });
    return;
  }

  res.writeHead(404);
  res.end("not found");
}

async function startServer(entry) {
  const server = createServer((req, res) => handleRequest(entry, req, res));
  await new Promise((r) => server.listen(0, "127.0.0.1", r));
  const addr = server.address();
  const port = typeof addr === "object" && addr ? addr.port : 0;
  entry.server = server;
  entry.port = port;
  entry.url = `http://127.0.0.1:${port}/`;
}

// --- canvas --------------------------------------------------------------

const canvas = createCanvas({
  id: "line-flex-viewer",
  displayName: "LINE Flex Message Viewer",
  description:
    "Preview and edit LINE Flex Message JSON with a live LINE-style render. Push JSON with set_content, read the user's edits with get_content.",
  inputSchema: {
    type: "object",
    properties: {
      docId: {
        type: "string",
        description: "Stable document id; reopening the same id restores content.",
      },
      content: { description: "Initial Flex Message JSON (message wrapper, bubble, or carousel)." },
      altText: { type: "string" },
    },
    additionalProperties: true,
  },
  actions: [
    {
      name: "set_content",
      description:
        "Set the Flex Message JSON shown in the canvas and re-render it. Accepts a full flex message ({type:'flex',altText,contents}), a bare bubble/carousel container, or a JSON string.",
      inputSchema: {
        type: "object",
        properties: {
          content: { description: "Flex Message JSON (object) or JSON string." },
          altText: { type: "string" },
        },
        required: ["content"],
        additionalProperties: true,
      },
      handler: async (ctx) => {
        const entry = instances.get(ctx.instanceId);
        if (!entry) throw new CanvasError("not_open", "canvas instance is not open");
        const content = normalizeContent(ctx.input && ctx.input.content);
        entry.content = content;
        await saveContent(entry.docId, content);
        broadcast(entry);
        const { valid, warnings } = validateContent(content);
        logFn(`set_content on ${entry.docId}${valid ? "" : " (with warnings)"}`);
        return { ok: true, valid, warnings };
      },
    },
    {
      name: "get_content",
      description:
        "Return the Flex Message JSON currently in the canvas, including any edits the user made in the panel.",
      inputSchema: { type: "object", properties: {}, additionalProperties: false },
      handler: async (ctx) => {
        const entry = instances.get(ctx.instanceId);
        if (!entry) throw new CanvasError("not_open", "canvas instance is not open");
        return { content: entry.content ?? null };
      },
    },
    {
      name: "validate",
      description:
        "Run a lightweight structural check on the current (or supplied) Flex Message JSON.",
      inputSchema: {
        type: "object",
        properties: {
          content: { description: "Optional JSON to validate instead of the current content." },
        },
        additionalProperties: true,
      },
      handler: async (ctx) => {
        const entry = instances.get(ctx.instanceId);
        let content = entry ? entry.content : null;
        if (ctx.input && ctx.input.content !== undefined) content = normalizeContent(ctx.input.content);
        if (content == null) return { valid: false, warnings: ["no content to validate"] };
        return validateContent(content);
      },
    },
  ],
  open: async (ctx) => {
    const input = ctx.input && typeof ctx.input === "object" ? ctx.input : {};
    const docId = safeDocId(input.docId || ctx.instanceId || "default");

    let entry = instances.get(ctx.instanceId);
    if (!entry) {
      entry = { instanceId: ctx.instanceId, docId, content: null, clients: new Set() };
      instances.set(ctx.instanceId, entry);
      await startServer(entry);
    }
    entry.docId = docId;

    // Seed content: explicit input wins, else durable storage, else keep as-is.
    if (input.content !== undefined && input.content !== null) {
      try {
        entry.content = normalizeContent(input.content);
        await saveContent(docId, entry.content);
      } catch (e) {
        logFn("open: invalid input.content ignored: " + (e && e.message));
      }
    } else if (entry.content == null) {
      entry.content = await loadContent(docId);
    }

    broadcast(entry);
    return { title: "LINE Flex Message Viewer", url: entry.url, status: docId };
  },
  onClose: async (ctx) => {
    const entry = instances.get(ctx.instanceId);
    if (!entry) return;
    instances.delete(ctx.instanceId);
    for (const res of entry.clients) {
      try {
        res.end();
      } catch {
        /* ignore */
      }
    }
    if (entry.server) await new Promise((r) => entry.server.close(() => r()));
  },
});

const session = await joinSession({ canvases: [canvas] });

logFn = (msg) => {
  // session.log returns a promise; swallow rejections so a logging failure
  // can never turn into an unhandled rejection that crashes the extension.
  try {
    const p = session.log(`[line-flex-viewer] ${msg}`, { level: "info", ephemeral: true });
    if (p && typeof p.catch === "function") p.catch(() => {});
  } catch {
    /* ignore */
  }
};

// Last-resort guard: never let an unexpected rejection crash the provider.
process.on("unhandledRejection", () => {});
