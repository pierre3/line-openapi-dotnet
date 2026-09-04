using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Line.OpenApi.Tools.Services;
using Xunit;

namespace Line.OpenApi.Tools.Tests;

/// <summary>
/// Tests for local asset serving in <see cref="FlexPreviewService"/>: the pure path
/// confinement logic (<see cref="FlexPreviewService.ResolveAssetPath"/>) and loopback
/// end-to-end fetches that prove a media file under LINE_FLEX_MCP_ASSET_DIR is actually
/// served while traversal and unsupported extensions are refused. The served set mirrors
/// what LINE renders in a Flex message: JPEG/PNG (incl. APNG) images and MP4 video.
/// </summary>
public sealed class FlexPreviewAssetServingTests
{
    // --- pure path confinement ----------------------------------------------

    [Fact]
    public void Disabled_when_no_directory_configured()
    {
        Assert.Null(FlexPreviewService.ResolveAssetPath(null, "/hero.png"));
        Assert.Null(FlexPreviewService.ResolveAssetPath("", "/hero.png"));
    }

    [Fact]
    public void Resolves_a_file_directly_under_the_directory()
    {
        using var dir = new TempDir();
        var file = dir.WriteFile("hero.png", "img");

        var resolved = FlexPreviewService.ResolveAssetPath(dir.Path, "/hero.png");

        Assert.Equal(Path.GetFullPath(file), resolved);
    }

    [Fact]
    public void Resolves_a_file_in_a_subdirectory()
    {
        using var dir = new TempDir();
        var file = dir.WriteFile(Path.Combine("assets", "hero.png"), "img");

        var resolved = FlexPreviewService.ResolveAssetPath(dir.Path, "/assets/hero.png");

        Assert.Equal(Path.GetFullPath(file), resolved);
    }

    [Theory]
    [InlineData("clip.mp4")]   // video component source
    [InlineData("photo.jpg")]
    [InlineData("photo.jpeg")]
    public void Resolves_supported_media_extensions(string name)
    {
        using var dir = new TempDir();
        var file = dir.WriteFile(name, "data");

        var resolved = FlexPreviewService.ResolveAssetPath(dir.Path, "/" + name);

        Assert.Equal(Path.GetFullPath(file), resolved);
    }

    [Fact]
    public void Percent_encoded_path_is_decoded()
    {
        using var dir = new TempDir();
        var file = dir.WriteFile(Path.Combine("my images", "a b.png"), "img");

        var resolved = FlexPreviewService.ResolveAssetPath(dir.Path, "/my%20images/a%20b.png");

        Assert.Equal(Path.GetFullPath(file), resolved);
    }

    [Fact]
    public void Missing_file_resolves_to_null()
    {
        using var dir = new TempDir();
        Assert.Null(FlexPreviewService.ResolveAssetPath(dir.Path, "/nope.png"));
    }

    [Theory]
    [InlineData("/notes.txt")]
    [InlineData("/archive.zip")]
    [InlineData("/config.json")]
    [InlineData("/hero")]
    [InlineData("/animation.gif")]  // GIF is not a LINE Flex format → refused
    [InlineData("/photo.webp")]     // WebP is not a LINE Flex format → refused
    public void Unsupported_extension_is_refused(string requestPath)
    {
        using var dir = new TempDir();
        // Even if such a file exists on disk, an unsupported extension must not be served.
        dir.WriteFile(requestPath.TrimStart('/'), "secret");
        Assert.Null(FlexPreviewService.ResolveAssetPath(dir.Path, requestPath));
    }

    [Theory]
    [InlineData("/../secret.png")]
    [InlineData("/../../secret.png")]
    [InlineData("/assets/../../secret.png")]
    [InlineData("/%2e%2e/secret.png")]
    [InlineData("/..%2fsecret.png")]
    [InlineData("/..%5csecret.png")]        // backslash-encoded traversal (Windows separator)
    [InlineData("/%2e%2e%2fsecret.png")]    // fully-encoded ../
    public void Traversal_outside_the_directory_is_refused(string requestPath)
    {
        using var dir = new TempDir();
        // Place the target one level above the served directory; it must stay unreachable.
        File.WriteAllText(Path.Combine(dir.Parent, "secret.png"), "secret");

        Assert.Null(FlexPreviewService.ResolveAssetPath(dir.Path, requestPath));
    }

    [Theory]
    [InlineData("/C:/Windows/System32/drivers/etc/hosts.png")] // rooted second segment: Combine discards base
    [InlineData("/\\\\server\\share\\x.png")]                   // UNC
    public void Rooted_or_absolute_segment_is_refused(string requestPath)
    {
        using var dir = new TempDir();
        Assert.Null(FlexPreviewService.ResolveAssetPath(dir.Path, requestPath));
    }

    [Fact]
    public void Uppercase_extension_is_accepted()
    {
        using var dir = new TempDir();
        var file = dir.WriteFile("LOGO.PNG", "img");

        var resolved = FlexPreviewService.ResolveAssetPath(dir.Path, "/LOGO.PNG");

        Assert.Equal(Path.GetFullPath(file), resolved);
    }

    [Fact]
    public void Symlink_pointing_outside_the_directory_is_refused()
    {
        using var dir = new TempDir();
        var outside = Path.Combine(dir.Parent, "secret.png");
        File.WriteAllText(outside, "secret");
        var link = Path.Combine(dir.Path, "evil.png");
        try { File.CreateSymbolicLink(link, outside); }
        catch { return; } // symlink creation not permitted here (no admin/dev mode) → skip

        // The lexical prefix check passes (the link sits under the dir), but resolving
        // the final target must reveal it escapes the directory and refuse it.
        Assert.Null(FlexPreviewService.ResolveAssetPath(dir.Path, "/evil.png"));
    }

    [Fact]
    public void Empty_or_root_path_is_refused()
    {
        using var dir = new TempDir();
        Assert.Null(FlexPreviewService.ResolveAssetPath(dir.Path, "/"));
        Assert.Null(FlexPreviewService.ResolveAssetPath(dir.Path, ""));
    }

    // --- content-type mapping -----------------------------------------------

    [Theory]
    [InlineData(".png", "image/png")]
    [InlineData(".PNG", "image/png")]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".mp4", "video/mp4")]
    [InlineData(".bin", "application/octet-stream")]
    public void Content_type_is_mapped_from_extension(string ext, string expected)
        => Assert.Equal(expected, FlexPreviewService.AssetContentType(ext));

    // --- loopback end-to-end -------------------------------------------------

    [Fact]
    public async Task Configured_media_is_served_over_loopback()
    {
        using var dir = new TempDir();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };     // PNG magic
        var mp4 = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 };     // ftyp box start
        var sub = dir.EnsureSub("assets");
        File.WriteAllBytes(Path.Combine(sub, "hero.png"), png);
        File.WriteAllBytes(Path.Combine(sub, "night sky.png"), png); // a space in the name
        File.WriteAllBytes(Path.Combine(sub, "promo.mp4"), mp4);

        using var scope = new EnvScope(("LINE_FLEX_MCP_NO_OPEN", "1"), ("LINE_FLEX_MCP_ASSET_DIR", dir.Path));
        using var service = new FlexPreviewService();
        var url = service.Open(); // starts the loopback server, returns http://127.0.0.1:<port>/
        var baseUri = new Uri(url);

        using var http = new HttpClient();

        // An image is served with the right bytes and content type.
        var image = await http.GetAsync(new Uri(baseUri, "assets/hero.png"));
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(png, await image.Content.ReadAsByteArrayAsync());

        // The video component's mp4 source is served as video/mp4.
        var video = await http.GetAsync(new Uri(baseUri, "assets/promo.mp4"));
        Assert.Equal(HttpStatusCode.OK, video.StatusCode);
        Assert.Equal("video/mp4", video.Content.Headers.ContentType?.MediaType);
        Assert.Equal(mp4, await video.Content.ReadAsByteArrayAsync());

        // A percent-encoded relative path (subdirectory + space) resolves end-to-end.
        var encoded = await http.GetAsync(new Uri(baseUri, "assets/night%20sky.png"));
        Assert.Equal(HttpStatusCode.OK, encoded.StatusCode);
        Assert.Equal(png, await encoded.Content.ReadAsByteArrayAsync());

        var missing = await http.GetAsync(new Uri(baseUri, "nope.png"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        // Raw-socket traversal: HttpClient/Uri normalize dot-segments before sending, so
        // assert confinement against a non-normalized wire path that reaches the server as-is.
        // Anything other than a 200 means the secret was not served (a 400/404, or a
        // connection reset from http.sys refusing the malformed target, all count as refused).
        var status = await RawGetStatusAsync(baseUri, "/assets/%2e%2e%2f%2e%2e%2fsecret.png");
        Assert.NotEqual(200, status);
    }

    [Fact]
    public async Task Media_is_not_served_when_directory_unconfigured()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "hero.png"), "img");

        using var scope = new EnvScope(("LINE_FLEX_MCP_NO_OPEN", "1"), ("LINE_FLEX_MCP_ASSET_DIR", null));
        using var service = new FlexPreviewService();
        var url = service.Open();

        using var http = new HttpClient();
        var res = await http.GetAsync(new Uri(new Uri(url), "hero.png"));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    // Send a raw HTTP/1.1 GET with an exact request-target (no client-side normalization)
    // and return the numeric status code, or -1 if the server refused/reset the connection.
    private static async Task<int> RawGetStatusAsync(Uri baseUri, string rawTarget)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(baseUri.Host, baseUri.Port);
            await using var stream = client.GetStream();
            var request = $"GET {rawTarget} HTTP/1.1\r\nHost: {baseUri.Host}:{baseUri.Port}\r\nConnection: close\r\n\r\n";
            var reqBytes = Encoding.ASCII.GetBytes(request);
            await stream.WriteAsync(reqBytes);
            using var reader = new StreamReader(stream, Encoding.ASCII);
            var statusLine = await reader.ReadLineAsync() ?? "";
            var parts = statusLine.Split(' ');
            return parts.Length >= 2 && int.TryParse(parts[1], out var code) ? code : -1;
        }
        catch (Exception e) when (e is SocketException or IOException)
        {
            return -1; // connection reset / refused — the target was not served.
        }
    }

    // --- helpers -------------------------------------------------------------

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }
        public string Parent => Directory.GetParent(Path)!.FullName;

        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "line-flex-asset-tests", Guid.NewGuid().ToString("N"), "root");
            Directory.CreateDirectory(Path);
        }

        public string EnsureSub(string sub)
        {
            var full = System.IO.Path.Combine(Path, sub);
            Directory.CreateDirectory(full);
            return full;
        }

        public string WriteFile(string relative, string content)
        {
            var full = System.IO.Path.Combine(Path, relative);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
            return full;
        }

        public void Dispose()
        {
            try { Directory.Delete(Directory.GetParent(Path)!.FullName, recursive: true); }
            catch { /* best effort */ }
        }
    }

    private sealed class EnvScope : IDisposable
    {
        private readonly (string Key, string? Prev)[] _saved;

        public EnvScope(params (string Key, string? Value)[] vars)
        {
            _saved = new (string, string?)[vars.Length];
            for (var i = 0; i < vars.Length; i++)
            {
                _saved[i] = (vars[i].Key, Environment.GetEnvironmentVariable(vars[i].Key));
                Environment.SetEnvironmentVariable(vars[i].Key, vars[i].Value);
            }
        }

        public void Dispose()
        {
            foreach (var (key, prev) in _saved)
                Environment.SetEnvironmentVariable(key, prev);
        }
    }
}
