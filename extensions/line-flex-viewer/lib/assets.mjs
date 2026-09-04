// Shared local-media serving for the LINE Flex viewer's loopback servers.
//
// The Copilot canvas (../extension.mjs) and the standalone MCP server
// (../mcp/server.mjs) both let a Flex message reference local artwork/video by a
// relative url (e.g. "assets/hero.png") during preview, then swap only the origin
// for the production HTTPS URL later. This is opt-in via LINE_FLEX_MCP_ASSET_DIR
// and confined to that directory.
//
// This module mirrors the .NET FlexPreviewService (ResolveAssetPath /
// AssetContentType / IsLoopbackHost) so the two servers resolve requests
// identically. The confinement logic is security-sensitive: keep it here in one
// place so the servers cannot diverge (a lesson from the spec-normalization
// double-implementation bug elsewhere in this repo).
//
// The served set matches what LINE actually renders in a Flex message: images are
// JPEG/PNG (APNG is .png) and videos are .mp4 (the video component). LINE itself
// does not render data:/local urls, so this is a preview-only convenience.

import { resolve as resolvePath, extname, sep } from "node:path";
import { statSync, lstatSync, realpathSync } from "node:fs";

// Only these extensions are served from the asset directory. Other formats
// (GIF, WebP, ...) are intentionally excluded because LINE does not render them
// in a Flex message.
export const ASSET_EXTENSIONS = new Set([".png", ".jpg", ".jpeg", ".mp4"]);

/** Map a file extension to the content type LINE-renderable media is served with. */
export function assetContentType(ext) {
  switch (String(ext).toLowerCase()) {
    case ".png":
      return "image/png";
    case ".jpg":
    case ".jpeg":
      return "image/jpeg";
    case ".mp4":
      return "video/mp4";
    default:
      return "application/octet-stream";
  }
}

/**
 * Normalize a LINE_FLEX_MCP_ASSET_DIR value to a full path once, or null when the
 * value is empty/blank. A relative value is resolved against the current directory,
 * so an absolute path is recommended. The directory need not exist yet (files may be
 * added later); a missing file simply resolves to null at request time.
 */
export function resolveAssetDir(value) {
  if (!value || !value.trim()) return null;
  try {
    return resolvePath(value);
  } catch {
    return null;
  }
}

/**
 * The Host header must be the loopback authority we bound to (127.0.0.1:<port>),
 * accepting the "localhost" alias for the same port. Anything else (a rebound DNS
 * name, another port) is rejected. Mirrors .NET IsLoopbackHost — used to blunt
 * DNS-rebinding reads of local media.
 */
export function isLoopbackHost(hostHeader, boundPort) {
  if (!hostHeader || !boundPort) return false;
  const colon = hostHeader.lastIndexOf(":");
  if (colon < 0) return false;
  const host = hostHeader.slice(0, colon);
  const port = hostHeader.slice(colon + 1);
  return (
    (host === "127.0.0.1" || host.toLowerCase() === "localhost") &&
    port === String(boundPort)
  );
}

/**
 * Resolve an HTTP request path (e.g. "/assets/hero.png") to a file under assetDir,
 * or null when serving is disabled, the extension is not an allowed media type, the
 * resolved path escapes the directory, or the file does not exist.
 *
 * Confinement is enforced by normalizing both the base and the combined path to full
 * paths and requiring the candidate to stay under the base — this rejects ../ traversal,
 * rooted/absolute segments, and backslash tricks regardless of the encoded form.
 * Mirrors .NET FlexPreviewService.ResolveAssetPath.
 */
export function resolveAssetPath(assetDir, requestPath) {
  if (!assetDir || !requestPath) return null;

  let decoded;
  try {
    decoded = decodeURIComponent(requestPath);
  } catch {
    return null; // malformed percent-encoding
  }

  // Reject NUL / control characters defensively before touching the filesystem.
  for (let i = 0; i < decoded.length; i++) {
    const code = decoded.charCodeAt(i);
    if (code < 0x20 || code === 0x7f) return null;
  }

  // Strip leading path separators (both / and \) so the remainder is relative.
  const relative = decoded.replace(/^[/\\]+/, "");
  if (relative.length === 0) return null;

  if (!ASSET_EXTENSIONS.has(extname(relative).toLowerCase())) return null;

  let fullBase, candidate;
  try {
    fullBase = resolvePath(assetDir);
    candidate = resolvePath(fullBase, relative);
  } catch {
    return null;
  }

  const baseWithSep = fullBase.endsWith(sep) ? fullBase : fullBase + sep;
  if (!candidate.startsWith(baseWithSep)) return null;

  // Must be an existing regular file.
  let st;
  try {
    st = statSync(candidate);
  } catch {
    return null;
  }
  if (!st.isFile()) return null;

  // Defense in depth: the prefix check above is lexical (resolvePath does not follow
  // links). If the entry itself is a symlink, resolve its final target and require that
  // to stay under the base too, so a link inside the directory cannot escape it.
  // Non-links are served as-is (mirrors .NET ResolveLinkTarget == null).
  try {
    if (lstatSync(candidate).isSymbolicLink()) {
      const real = realpathSync(candidate);
      if (real !== fullBase && !real.startsWith(baseWithSep)) return null;
    }
  } catch {
    return null; // if the link target cannot be verified, refuse to serve.
  }

  return candidate;
}

/**
 * One-call helper for the loopback servers: given the request path, the incoming Host
 * header, the bound port, and the configured asset dir, return { file, contentType } to
 * serve, or null to fall through to 404. Encapsulates the loopback-host guard plus path
 * confinement so the canvas and MCP servers share one identical, tested code path.
 */
export function resolveMediaRequest({ path, host, boundPort, assetDir }) {
  if (!assetDir) return null;
  if (!isLoopbackHost(host, boundPort)) return null;
  const file = resolveAssetPath(assetDir, path);
  if (!file) return null;
  return { file, contentType: assetContentType(extname(file)) };
}
