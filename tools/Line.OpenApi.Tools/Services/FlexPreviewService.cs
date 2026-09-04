using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Line.OpenApi.Tools.Services;

/// <summary>
/// Hosts a local, loopback-only web preview of a LINE Flex Message and keeps it live.
///
/// The browser-side renderer (renderer.js + flex.css) is the same one used by the
/// Copilot canvas / standalone viewer, embedded here as assembly resources
/// (<c>web/*</c>). This service mirrors the zero-dependency Node MCP server: it
/// serves the viewer page over an ephemeral <c>127.0.0.1</c> port, exposes
/// <c>/api/state</c> (GET/POST) and <c>/api/events</c> (SSE), opens the default
/// browser once, and pushes updates to already-open tabs.
///
/// It performs no LINE API calls and stores no secrets, so it is safe under
/// <c>--read-only</c>. State (the current Flex JSON) is persisted to a temp file
/// so a reopened tab restores the last preview.
///
/// When <c>LINE_FLEX_MCP_ASSET_DIR</c> is set, media files under that directory are
/// also served, so a Flex message can reference local artwork by a relative <c>url</c>
/// (e.g. <c>"assets/hero.png"</c>) during preview and swap only the origin for the
/// production HTTPS URL later. Serving is opt-in (disabled unless the directory is
/// configured) and confined to that directory (path traversal is rejected). The served
/// set matches what LINE actually renders in a Flex message: images are JPEG/PNG (APNG
/// is <c>.png</c>) and videos are <c>.mp4</c> (the <c>video</c> component). LINE itself
/// does not render <c>data:</c>/local URLs, so this is a preview-only convenience.
/// </summary>
internal sealed class FlexPreviewService : IDisposable
{
    private static readonly string[] StaticFiles =
        { "viewer.html", "viewer.js", "renderer.js", "flex.css", "samples.js" };

    // Only these extensions are served from the asset directory. The set mirrors the
    // formats LINE renders in a Flex message: JPEG/PNG (incl. APNG) for the image
    // component and MP4 for the video component. Other formats are intentionally excluded.
    private static readonly HashSet<string> AssetExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".mp4" };

    private readonly object _gate = new();
    private readonly ConcurrentDictionary<Guid, HttpListenerResponse> _clients = new();
    private readonly string _stateFile;
    private readonly string? _assetDir;
    private readonly bool _autoOpen;

    private HttpListener? _listener;
    private string? _url;
    private JsonNode? _content;
    private bool _opened;

    public FlexPreviewService()
    {
        var stateDir = Environment.GetEnvironmentVariable("LINE_FLEX_MCP_STATE_DIR")
            ?? Path.Combine(Path.GetTempPath(), "line-flex-mcp");
        _stateFile = Path.Combine(stateDir, "content.json");
        _autoOpen = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LINE_FLEX_MCP_NO_OPEN"));

        // Opt-in local asset serving. Normalize to a full path once so request-time
        // confinement compares against a stable base. A relative value is resolved
        // against the current directory at construction time, so an absolute path is
        // recommended. The directory need not exist yet (files may be added later);
        // a missing file simply 404s.
        var assetDir = Environment.GetEnvironmentVariable("LINE_FLEX_MCP_ASSET_DIR");
        _assetDir = string.IsNullOrWhiteSpace(assetDir) ? null : SafeFullPath(assetDir);
    }

    // --- public API (called by the MCP tools) --------------------------------

    /// <summary>
    /// Set the Flex JSON to preview, (re)start the server, push it to any open
    /// tab, and open the browser on first use.
    /// </summary>
    public PreviewResult Preview(string contentJson, string? altText)
    {
        var node = Normalize(contentJson);
        var (valid, warnings) = Validate(node);

        lock (_gate) { _content = node; }
        SaveContent(node);

        var url = EnsureServer();
        Broadcast(node);

        var opened = false;
        lock (_gate)
        {
            if (_autoOpen && !_opened)
            {
                _opened = true;
                opened = true;
            }
        }
        if (opened) OpenBrowser(url);

        return new PreviewResult(true, url, valid, warnings, opened);
    }

    /// <summary>Return the JSON currently shown in the preview, including the user's browser edits.</summary>
    public JsonNode? GetContent()
    {
        lock (_gate)
        {
            if (_content is not null) return _content.DeepClone();
        }
        var loaded = LoadContent();
        lock (_gate) { _content = loaded; }
        return loaded?.DeepClone();
    }

    /// <summary>Structurally validate the supplied JSON, or the current preview content when null.</summary>
    public (bool Valid, IReadOnlyList<string> Warnings) ValidateInput(string? contentJson)
    {
        JsonNode? node;
        if (string.IsNullOrWhiteSpace(contentJson))
        {
            node = GetContent();
            if (node is null) return (false, new[] { "no content to validate" });
        }
        else
        {
            // A "validate" tool should report bad input structurally, not throw.
            try { node = Normalize(contentJson); }
            catch (Exception e) { return (false, new[] { e.Message }); }
        }
        return Validate(node);
    }

    /// <summary>
    /// Ensure the server is running, (re)open it in the browser, and return the preview URL.
    /// Unlike <see cref="Preview"/> (which opens at most once automatically), this is an
    /// explicit user gesture, so it always opens — useful when the tab was closed.
    /// </summary>
    public string Open()
    {
        var url = EnsureServer();
        lock (_gate) { _opened = true; }
        if (_autoOpen) OpenBrowser(url);
        return url;
    }

    // --- persistence ---------------------------------------------------------

    private JsonNode? LoadContent()
    {
        try
        {
            var raw = File.ReadAllText(_stateFile);
            var parsed = JsonNode.Parse(raw);
            if (parsed is JsonObject obj && obj.TryGetPropertyValue("content", out var inner))
                return inner?.DeepClone();
            return parsed;
        }
        catch
        {
            return null;
        }
    }

    private void SaveContent(JsonNode? content)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_stateFile)!);
            var wrapper = new JsonObject { ["content"] = content?.DeepClone() };
            File.WriteAllText(_stateFile, wrapper.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Persistence is best-effort; a failure must not break the preview.
        }
    }

    // Accept a message wrapper, a bare container, or a JSON string; return a JsonNode.
    private static JsonNode Normalize(string contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
            throw new ArgumentException("content must be a non-empty JSON string.");
        var node = JsonNode.Parse(contentJson)
            ?? throw new ArgumentException("content parsed to null.");
        if (node is not JsonObject && node is not JsonArray)
            throw new ArgumentException("content must be a JSON object or array.");
        return node;
    }

    // --- structural validation (ported from the shared renderer) -------------

    private static JsonObject? ExtractContainer(JsonNode? json)
    {
        if (json is not JsonObject obj) return null;
        var type = (string?)obj["type"];
        if (type == "flex" && obj["contents"] is JsonObject flexContents) return flexContents;
        if (type is "bubble" or "carousel") return obj;
        if (obj["contents"] is JsonObject c && ((string?)c["type"]) is "bubble" or "carousel") return c;
        return null;
    }

    private static (bool Valid, IReadOnlyList<string> Warnings) Validate(JsonNode? json)
    {
        var warnings = new List<string>();
        var container = ExtractContainer(json);
        if (container is null)
        {
            warnings.Add("root must be a \"bubble\"/\"carousel\" container or a type:\"flex\" message");
            return (false, warnings);
        }

        var bubbles = new List<JsonNode?>();
        if ((string?)container["type"] == "carousel")
        {
            if (container["contents"] is not JsonArray arr || arr.Count == 0)
            {
                warnings.Add("carousel.contents is empty");
            }
            else
            {
                if (arr.Count > 12) warnings.Add("carousel supports at most 12 bubbles");
                bubbles.AddRange(arr);
            }
        }
        else
        {
            bubbles.Add(container);
        }

        for (var i = 0; i < bubbles.Count; i++)
        {
            if (bubbles[i] is not JsonObject b || (string?)b["type"] != "bubble")
            {
                warnings.Add($"contents[{i}] is not a bubble");
                continue;
            }
            if (b["header"] is null && b["hero"] is null && b["body"] is null && b["footer"] is null)
                warnings.Add($"bubble[{i}] has no header/hero/body/footer block");
        }

        return (warnings.Count == 0, warnings);
    }

    // --- HTTP server ---------------------------------------------------------

    private string EnsureServer()
    {
        lock (_gate)
        {
            if (_listener is not null && _url is not null) return _url;
            if (_content is null) _content = LoadContent();

            var port = FreeLoopbackPort();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            _listener = listener;
            _url = $"http://127.0.0.1:{port}/";

            _ = Task.Run(() => AcceptLoop(listener));
            return _url;
        }
    }

    // The Host header must be the loopback authority we bound to (127.0.0.1:<port>),
    // accepting the "localhost" alias for the same port. Anything else (a rebound DNS
    // name, another port) is rejected.
    private bool IsLoopbackHost(string? hostHeader)
    {
        string? url;
        lock (_gate) { url = _url; }
        if (url is null || string.IsNullOrEmpty(hostHeader)) return false;
        var boundPort = new Uri(url).Port;
        var colon = hostHeader.LastIndexOf(':');
        if (colon < 0) return false;
        var host = hostHeader[..colon];
        var portText = hostHeader[(colon + 1)..];
        return (host.Equals("127.0.0.1", StringComparison.Ordinal)
                || host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            && portText == boundPort.ToString();
    }

    // For state-mutating POSTs, when an Origin header is present it must be same-origin
    // (loopback authority). Absent Origin (non-browser clients) is allowed; the Host
    // check above still applies.
    private bool IsAllowedOrigin(string? origin)
    {
        if (string.IsNullOrEmpty(origin)) return true;
        return Uri.TryCreate(origin, UriKind.Absolute, out var o) && IsLoopbackHost(o.Authority);
    }

    private static int FreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task AcceptLoop(HttpListener listener)
    {
        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync().ConfigureAwait(false); }
            catch { break; }
            _ = Task.Run(() => HandleRequest(ctx));
        }
    }

    private void HandleRequest(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        var path = req.Url?.AbsolutePath ?? "/";

        try
        {
            if (req.HttpMethod == "GET" && (path == "/" || path == "/index.html"))
            {
                var html = RenderIndex(ReadResource("viewer.html"));
                WriteText(res, 200, html, "text/html; charset=utf-8");
                return;
            }

            if (req.HttpMethod == "GET" && !path.StartsWith("/api/"))
            {
                var name = path.TrimStart('/');
                if (Array.IndexOf(StaticFiles, name) >= 0)
                {
                    WriteText(res, 200, ReadResource(name), ContentType(name));
                    return;
                }
                // Serve local media (opt-in via LINE_FLEX_MCP_ASSET_DIR) so a Flex
                // message can reference artwork/video by a relative url. Apply the same
                // loopback-host guard as /api to blunt DNS-rebinding reads.
                if (_assetDir is not null && IsLoopbackHost(req.UserHostName))
                {
                    var file = ResolveAssetPath(_assetDir, path);
                    if (file is not null)
                    {
                        WriteBytes(res, 200, File.ReadAllBytes(file), AssetContentType(Path.GetExtension(file)));
                        return;
                    }
                }
                WriteText(res, 404, "not found", "text/plain");
                return;
            }

            // Guard the /api/* endpoints against DNS-rebinding reads and cross-origin
            // writes (CSRF). Browsers always send Host; a rebound page or a foreign
            // origin will not match the loopback authority we bound to.
            if (path.StartsWith("/api/", StringComparison.Ordinal))
            {
                if (!IsLoopbackHost(req.UserHostName)
                    || (req.HttpMethod == "POST" && !IsAllowedOrigin(req.Headers["Origin"])))
                {
                    WriteText(res, 403, "forbidden", "text/plain");
                    return;
                }
            }

            if (req.HttpMethod == "GET" && path == "/api/state")
            {
                JsonNode? content;
                lock (_gate) { content = _content?.DeepClone(); }
                var body = new JsonObject { ["docId"] = "default", ["content"] = content };
                WriteText(res, 200, body.ToJsonString(), "application/json; charset=utf-8");
                return;
            }

            if (req.HttpMethod == "POST" && path == "/api/state")
            {
                string raw;
                using (var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8))
                    raw = reader.ReadToEnd();
                try
                {
                    var data = JsonNode.Parse(string.IsNullOrEmpty(raw) ? "{}" : raw) as JsonObject;
                    var content = data? ["content"]?.DeepClone();
                    lock (_gate) { _content = content; }
                    SaveContent(content);
                    WriteText(res, 200, "{\"ok\":true}", "application/json; charset=utf-8");
                }
                catch (Exception e)
                {
                    var err = new JsonObject { ["ok"] = false, ["error"] = e.Message };
                    WriteText(res, 400, err.ToJsonString(), "application/json; charset=utf-8");
                }
                return;
            }

            if (req.HttpMethod == "GET" && path == "/api/events")
            {
                ServeEvents(res);
                return; // response is kept open by ServeEvents
            }

            WriteText(res, 404, "not found", "text/plain");
        }
        catch
        {
            try { WriteText(res, 500, "error", "text/plain"); } catch { /* ignore */ }
        }
    }

    private void ServeEvents(HttpListenerResponse res)
    {
        res.StatusCode = 200;
        res.SendChunked = true;
        res.ContentType = "text/event-stream";
        res.Headers["Cache-Control"] = "no-cache";
        res.KeepAlive = true;

        var id = Guid.NewGuid();
        _clients[id] = res;
        try
        {
            WriteRaw(res, ": connected\n\n");
            // Push the current content immediately so a freshly opened tab renders at once.
            JsonNode? current;
            lock (_gate) { current = _content?.DeepClone(); }
            if (current is not null) WriteRaw(res, ContentEvent(current));
        }
        catch
        {
            _clients.TryRemove(id, out _);
            try { res.Close(); } catch { /* ignore */ }
        }
        // The response stays open; Broadcast() writes to it until the client disconnects.
    }

    private void Broadcast(JsonNode? content)
    {
        var payload = ContentEvent(content);
        foreach (var (id, res) in _clients)
        {
            try { WriteRaw(res, payload); }
            catch
            {
                _clients.TryRemove(id, out _);
                try { res.Close(); } catch { /* ignore */ }
            }
        }
    }

    private static string ContentEvent(JsonNode? content)
    {
        var data = new JsonObject { ["content"] = content?.DeepClone() };
        return $"event: content\ndata: {data.ToJsonString()}\n\n";
    }

    private static void WriteRaw(HttpListenerResponse res, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        res.OutputStream.Write(bytes, 0, bytes.Length);
        res.OutputStream.Flush();
    }

    private static void WriteText(HttpListenerResponse res, int status, string text, string contentType)
        => WriteBytes(res, status, Encoding.UTF8.GetBytes(text), contentType);

    private static void WriteBytes(HttpListenerResponse res, int status, byte[] bytes, string contentType)
    {
        res.StatusCode = status;
        res.ContentType = contentType;
        res.ContentLength64 = bytes.Length;
        res.OutputStream.Write(bytes, 0, bytes.Length);
        res.OutputStream.Close();
    }

    // Inject the viewer config before the first script, exactly like the Node server.
    private static string RenderIndex(string html)
    {
        const string marker = "<script src=\"./samples.js\"></script>";
        const string cfg = "<script>window.__FLEX_CFG__={\"docId\":\"default\",\"instanceId\":\"mcp\"};</script>";
        return html.Replace(marker, cfg + "\n    " + marker);
    }

    private static string ContentType(string name) => Path.GetExtension(name) switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        _ => "application/octet-stream",
    };

    // --- local asset serving -------------------------------------------------

    // Internal for unit testing the extension → content-type mapping.
    internal static string AssetContentType(string ext) => ext.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".mp4" => "video/mp4",
        _ => "application/octet-stream",
    };

    private static string? SafeFullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return null; }
    }

    /// <summary>
    /// Resolve an HTTP request path (e.g. <c>"/assets/hero.png"</c>) to a file under the
    /// configured asset directory, or <c>null</c> when serving is disabled, the extension is
    /// not an allowed media type, the resolved path escapes the directory, or the file does
    /// not exist. Confinement is enforced by normalizing both the base and the combined path
    /// to full paths and requiring the candidate to stay under the base — this rejects
    /// <c>../</c> traversal, rooted/absolute segments, and backslash tricks regardless of the
    /// encoded form. Internal for unit testing.
    /// </summary>
    internal static string? ResolveAssetPath(string? assetDir, string requestPath)
    {
        if (string.IsNullOrEmpty(assetDir) || string.IsNullOrEmpty(requestPath)) return null;

        var decoded = Uri.UnescapeDataString(requestPath);
        // Reject NUL / control characters defensively before touching the filesystem.
        foreach (var c in decoded)
            if (char.IsControl(c)) return null;

        var relative = decoded.TrimStart('/');
        if (relative.Length == 0) return null;

        if (!AssetExtensions.Contains(Path.GetExtension(relative))) return null;

        string fullBase, candidate;
        try
        {
            fullBase = Path.GetFullPath(assetDir);
            candidate = Path.GetFullPath(Path.Combine(fullBase, relative));
        }
        catch
        {
            return null;
        }

        // candidate is built from fullBase, so the base portion shares its casing;
        // an Ordinal prefix check is both correct and case-safe here.
        var baseWithSep = fullBase.EndsWith(Path.DirectorySeparatorChar)
            ? fullBase
            : fullBase + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(baseWithSep, StringComparison.Ordinal)) return null;

        if (!File.Exists(candidate)) return null;

        // The prefix check above is lexical (Path.GetFullPath does not resolve links).
        // Defense in depth: if the entry is a symlink/junction, resolve its final target
        // and require that to stay under the base too, so a link inside the directory
        // cannot escape it. Non-links resolve to null and are served as-is.
        try
        {
            var target = File.ResolveLinkTarget(candidate, returnFinalTarget: true);
            if (target is not null)
            {
                var real = Path.GetFullPath(target.FullName);
                if (!real.StartsWith(baseWithSep, StringComparison.Ordinal)) return null;
            }
        }
        catch
        {
            return null; // if the link target cannot be verified, refuse to serve.
        }

        return candidate;
    }

    // --- embedded web assets -------------------------------------------------

    private static string ReadResource(string name)
    {
        var asm = typeof(FlexPreviewService).Assembly;
        var suffix = ".web." + name;
        var resource = Array.Find(asm.GetManifestResourceNames(), n => n.EndsWith(suffix, StringComparison.Ordinal))
            ?? throw new FileNotFoundException($"Embedded web asset not found: {name}");
        using var stream = asm.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // --- browser open --------------------------------------------------------

    private static void OpenBrowser(string url)
    {
        try
        {
            System.Diagnostics.ProcessStartInfo psi;
            if (OperatingSystem.IsWindows())
                psi = new System.Diagnostics.ProcessStartInfo("cmd", $"/c start \"\" \"{url}\"") { CreateNoWindow = true };
            else if (OperatingSystem.IsMacOS())
                psi = new System.Diagnostics.ProcessStartInfo("open", url);
            else
                psi = new System.Diagnostics.ProcessStartInfo("xdg-open", url);
            psi.UseShellExecute = false;
            System.Diagnostics.Process.Start(psi);
        }
        catch
        {
            // The URL is still returned to the caller; opening is best-effort.
        }
    }

    public void Dispose()
    {
        // Close any open SSE streams so a shutdown does not leak held responses.
        foreach (var (id, res) in _clients)
        {
            _clients.TryRemove(id, out _);
            try { res.Close(); } catch { /* ignore */ }
        }
        try { _listener?.Stop(); } catch { /* ignore */ }
        try { _listener?.Close(); } catch { /* ignore */ }
    }

    public readonly record struct PreviewResult(
        bool Ok, string Url, bool Valid, IReadOnlyList<string> Warnings, bool Opened);
}
