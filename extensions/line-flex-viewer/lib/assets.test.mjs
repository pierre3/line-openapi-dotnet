// Tests for local asset serving shared by the LINE Flex viewer's loopback servers
// (../extension.mjs canvas + ../mcp/server.mjs). Mirrors the .NET
// FlexPreviewAssetServingTests: the pure path-confinement logic (resolveAssetPath),
// the loopback host guard (isLoopbackHost / resolveMediaRequest), the content-type
// map, and loopback end-to-end fetches proving a media file under
// LINE_FLEX_MCP_ASSET_DIR is actually served while traversal, unsupported extensions,
// and cross-host (DNS-rebinding) reads are refused.
//
// Zero dependencies: run with `node --test lib/assets.test.mjs` (Node 18+).

import test, { after } from "node:test";
import assert from "node:assert/strict";
import { createServer } from "node:http";
import { connect } from "node:net";
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, symlinkSync } from "node:fs";
import { rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join, resolve as resolvePath } from "node:path";

import {
  resolveAssetPath,
  resolveAssetDir,
  assetContentType,
  isLoopbackHost,
  resolveMediaRequest,
} from "./assets.mjs";

// --- helpers ---------------------------------------------------------------

// One temp base for the whole run, removed once at the end. Per-test cleanup is
// deliberately avoided: synchronous recursive removal (fs.rmSync) hard-crashes some
// Windows/Node builds when the temp path contains non-ASCII characters, so we defer
// to a single async fs.rm in after().
const BASE = mkdtempSync(join(tmpdir(), "line-flex-asset-"));

after(async () => {
  await rm(BASE, { recursive: true, force: true });
});

// A temp tree per test: `root` is the served directory, `parent` sits one level above
// it (where "escaped" targets are planted so traversal must not reach them).
function makeTempDir() {
  const parent = mkdtempSync(join(BASE, "d-"));
  const root = join(parent, "root");
  mkdirSync(root, { recursive: true });
  return {
    path: root,
    parent,
    writeFile(relative, content) {
      const full = join(root, relative);
      mkdirSync(join(full, ".."), { recursive: true });
      writeFileSync(full, content);
      return full;
    },
  };
}

function rawRequest(port, rawTarget, hostHeader) {
  return new Promise((resolve, reject) => {
    const socket = connect(port, "127.0.0.1", () => {
      const host = hostHeader ?? `127.0.0.1:${port}`;
      socket.write(`GET ${rawTarget} HTTP/1.1\r\nHost: ${host}\r\nConnection: close\r\n\r\n`);
    });
    const chunks = [];
    socket.on("data", (c) => chunks.push(c));
    socket.on("end", () => {
      const buf = Buffer.concat(chunks);
      const sepIdx = buf.indexOf("\r\n\r\n");
      const headerText = buf.slice(0, sepIdx < 0 ? buf.length : sepIdx).toString("latin1");
      const body = sepIdx < 0 ? Buffer.alloc(0) : buf.slice(sepIdx + 4);
      const lines = headerText.split("\r\n");
      const status = parseInt((lines[0] || "").split(" ")[1], 10) || -1;
      const headers = {};
      for (let i = 1; i < lines.length; i++) {
        const idx = lines[i].indexOf(":");
        if (idx > 0) headers[lines[i].slice(0, idx).trim().toLowerCase()] = lines[i].slice(idx + 1).trim();
      }
      resolve({ status, headers, body });
    });
    socket.on("error", reject);
    socket.setTimeout(5000, () => { socket.destroy(); reject(new Error("timeout")); });
  });
}

// A minimal loopback server wired with the exact helper both real servers use.
function startMediaServer(assetDir) {
  return new Promise((resolve) => {
    const server = createServer((req, res) => {
      const path = new URL(req.url, "http://127.0.0.1").pathname;
      const media = resolveMediaRequest({
        path,
        host: req.headers.host,
        boundPort: server.address().port,
        assetDir,
      });
      if (media) {
        // Set Content-Length so the test's raw-socket reader gets the body verbatim
        // (without it Node uses chunked transfer-encoding, which the reader does not decode).
        const buf = readFileSync(media.file);
        res.writeHead(200, { "Content-Type": media.contentType, "Content-Length": buf.length });
        res.end(buf);
        return;
      }
      res.writeHead(404);
      res.end("not found");
    });
    server.listen(0, "127.0.0.1", () => resolve({ server, port: server.address().port }));
  });
}

// --- resolveAssetDir -------------------------------------------------------

test("resolveAssetDir: null/blank disables serving", () => {
  assert.equal(resolveAssetDir(undefined), null);
  assert.equal(resolveAssetDir(""), null);
  assert.equal(resolveAssetDir("   "), null);
});

test("resolveAssetDir: a value normalizes to a full path", () => {
  assert.equal(resolveAssetDir("."), resolvePath("."));
});

// --- pure path confinement -------------------------------------------------

test("disabled when no directory configured", () => {
  assert.equal(resolveAssetPath(null, "/hero.png"), null);
  assert.equal(resolveAssetPath("", "/hero.png"), null);
});

test("resolves a file directly under the directory", () => {
  const dir = makeTempDir();
  const file = dir.writeFile("hero.png", "img");
  assert.equal(resolveAssetPath(dir.path, "/hero.png"), resolvePath(file));
});

test("resolves a file in a subdirectory", () => {
  const dir = makeTempDir();
  const file = dir.writeFile(join("assets", "hero.png"), "img");
  assert.equal(resolveAssetPath(dir.path, "/assets/hero.png"), resolvePath(file));
});

for (const name of ["clip.mp4", "photo.jpg", "photo.jpeg"]) {
  test(`resolves supported media extension: ${name}`, () => {
    const dir = makeTempDir();
    const file = dir.writeFile(name, "data");
    assert.equal(resolveAssetPath(dir.path, "/" + name), resolvePath(file));
  });
}

test("percent-encoded path is decoded", () => {
  const dir = makeTempDir();
  const file = dir.writeFile(join("my images", "a b.png"), "img");
  assert.equal(resolveAssetPath(dir.path, "/my%20images/a%20b.png"), resolvePath(file));
});

test("missing file resolves to null", () => {
  const dir = makeTempDir();
  assert.equal(resolveAssetPath(dir.path, "/nope.png"), null);
});

for (const requestPath of ["/notes.txt", "/archive.zip", "/config.json", "/hero", "/animation.gif", "/photo.webp"]) {
  test(`unsupported extension is refused: ${requestPath}`, () => {
    const dir = makeTempDir();
    // Even if such a file exists on disk, an unsupported extension must not be served.
    dir.writeFile(requestPath.replace(/^\//, ""), "secret");
    assert.equal(resolveAssetPath(dir.path, requestPath), null);
  });
}

for (const requestPath of [
  "/../secret.png",
  "/../../secret.png",
  "/assets/../../secret.png",
  "/%2e%2e/secret.png",
  "/..%2fsecret.png",
  "/..%5csecret.png",       // backslash-encoded traversal (Windows separator)
  "/%2e%2e%2fsecret.png",   // fully-encoded ../
]) {
  test(`traversal outside the directory is refused: ${requestPath}`, () => {
    const dir = makeTempDir();
    writeFileSync(join(dir.parent, "secret.png"), "secret");
    assert.equal(resolveAssetPath(dir.path, requestPath), null);
  });
}

for (const requestPath of [
  "/C:/Windows/System32/drivers/etc/hosts.png", // rooted second segment
  "/\\\\server\\share\\x.png",                  // UNC
]) {
  test(`rooted or absolute segment is refused: ${requestPath}`, () => {
    const dir = makeTempDir();
    assert.equal(resolveAssetPath(dir.path, requestPath), null);
  });
}

test("control characters are refused", () => {
  const dir = makeTempDir();
  assert.equal(resolveAssetPath(dir.path, "/hero%00.png"), null);
});

test("uppercase extension is accepted", () => {
  const dir = makeTempDir();
  const file = dir.writeFile("LOGO.PNG", "img");
  assert.equal(resolveAssetPath(dir.path, "/LOGO.PNG"), resolvePath(file));
});

test("symlink pointing outside the directory is refused", () => {
  const dir = makeTempDir();
  const outside = join(dir.parent, "secret.png");
  writeFileSync(outside, "secret");
  const link = join(dir.path, "evil.png");
  try { symlinkSync(outside, link); }
  catch { return; } // symlink creation not permitted here (no admin/dev mode) → skip
  // The lexical prefix check passes (the link sits under the dir), but resolving the
  // final target must reveal it escapes the directory and refuse it.
  assert.equal(resolveAssetPath(dir.path, "/evil.png"), null);
});

test("empty or root path is refused", () => {
  const dir = makeTempDir();
  assert.equal(resolveAssetPath(dir.path, "/"), null);
  assert.equal(resolveAssetPath(dir.path, ""), null);
});

// --- content-type mapping --------------------------------------------------

for (const [ext, expected] of [
  [".png", "image/png"],
  [".PNG", "image/png"],
  [".jpg", "image/jpeg"],
  [".jpeg", "image/jpeg"],
  [".mp4", "video/mp4"],
  [".bin", "application/octet-stream"],
]) {
  test(`content type from extension: ${ext} → ${expected}`, () => {
    assert.equal(assetContentType(ext), expected);
  });
}

// --- loopback host guard ---------------------------------------------------

test("isLoopbackHost accepts the bound loopback authority", () => {
  assert.equal(isLoopbackHost("127.0.0.1:8080", 8080), true);
  assert.equal(isLoopbackHost("localhost:8080", 8080), true);
  assert.equal(isLoopbackHost("LocalHost:8080", 8080), true);
});

test("isLoopbackHost rejects other hosts, ports, and missing values", () => {
  assert.equal(isLoopbackHost("evil.example.com:8080", 8080), false);
  assert.equal(isLoopbackHost("127.0.0.1:9999", 8080), false);
  assert.equal(isLoopbackHost("127.0.0.1", 8080), false); // no port
  assert.equal(isLoopbackHost("", 8080), false);
  assert.equal(isLoopbackHost("127.0.0.1:8080", null), false);
});

// --- resolveMediaRequest (the shared server code path) ---------------------

test("resolveMediaRequest: null when serving disabled", () => {
  assert.equal(resolveMediaRequest({ path: "/hero.png", host: "127.0.0.1:80", boundPort: 80, assetDir: null }), null);
});

test("resolveMediaRequest: null when host is not the bound loopback", () => {
  const dir = makeTempDir();
  dir.writeFile("hero.png", "img");
  // File exists and would resolve, but a foreign Host header must refuse it.
  assert.equal(
    resolveMediaRequest({ path: "/hero.png", host: "evil.example.com:80", boundPort: 80, assetDir: dir.path }),
    null
  );
});

test("resolveMediaRequest: returns file and content type when allowed", () => {
  const dir = makeTempDir();
  const file = dir.writeFile("hero.png", "img");
  const media = resolveMediaRequest({ path: "/hero.png", host: "127.0.0.1:80", boundPort: 80, assetDir: dir.path });
  assert.deepEqual(media, { file: resolvePath(file), contentType: "image/png" });
});

// --- loopback end-to-end ---------------------------------------------------

test("configured media is served over loopback", async () => {
  const dir = makeTempDir();
  const png = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]); // PNG magic
  const mp4 = Buffer.from([0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70]); // ftyp box start
  dir.writeFile(join("assets", "hero.png"), png);
  dir.writeFile(join("assets", "night sky.png"), png); // a space in the name
  dir.writeFile(join("assets", "promo.mp4"), mp4);
  writeFileSync(join(dir.parent, "secret.png"), "secret"); // one level above the served dir

  const { server, port } = await startMediaServer(dir.path);
  try {
    const image = await rawRequest(port, "/assets/hero.png");
    assert.equal(image.status, 200);
    assert.equal(image.headers["content-type"], "image/png");
    assert.deepEqual(image.body, png);

    const video = await rawRequest(port, "/assets/promo.mp4");
    assert.equal(video.status, 200);
    assert.equal(video.headers["content-type"], "video/mp4");
    assert.deepEqual(video.body, mp4);

    const encoded = await rawRequest(port, "/assets/night%20sky.png");
    assert.equal(encoded.status, 200);
    assert.deepEqual(encoded.body, png);

    const missing = await rawRequest(port, "/nope.png");
    assert.equal(missing.status, 404);

    // Raw-socket traversal that reaches the server as-is (no client normalization):
    // anything other than 200 means the secret was not served.
    const traversal = await rawRequest(port, "/assets/%2e%2e%2f%2e%2e%2fsecret.png");
    assert.notEqual(traversal.status, 200);

    // DNS-rebinding read: a foreign Host header for the same loopback port is refused.
    const rebind = await rawRequest(port, "/assets/hero.png", `evil.example.com:${port}`);
    assert.equal(rebind.status, 404);
  } finally {
    server.close();
  }
});

test("media is not served when directory unconfigured", async () => {
  const dir = makeTempDir();
  dir.writeFile("hero.png", "img");
  const { server, port } = await startMediaServer(null); // assetDir disabled
  try {
    const res = await rawRequest(port, "/hero.png");
    assert.equal(res.status, 404);
  } finally {
    server.close();
  }
});
