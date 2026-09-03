using System.Security.Cryptography;
using Xunit;

namespace Line.OpenApi.Tools.Tests;

/// <summary>
/// The Flex renderer is shared across four surfaces (Copilot canvas, bundled Node MCP,
/// the .NET line_flex_* tools, and the standalone page). The .NET tool embeds a subset
/// under <c>tools/Line.OpenApi.Tools/web/</c>, and the canvas extension keeps its own
/// copy under <c>extensions/line-flex-viewer/web/</c>. Until the two are single-sourced,
/// this guard fails the build if the shared subset ever drifts apart.
/// </summary>
public sealed class FlexWebAssetsParityTests
{
    private static readonly string[] SharedAssets =
        { "renderer.js", "flex.css", "samples.js", "viewer.html", "viewer.js" };

    [Fact]
    public void Shared_web_assets_are_byte_identical_across_surfaces()
    {
        var root = RepoRoot();
        var toolsWeb = Path.Combine(root, "tools", "Line.OpenApi.Tools", "web");
        var extWeb = Path.Combine(root, "extensions", "line-flex-viewer", "web");

        foreach (var name in SharedAssets)
        {
            var a = Path.Combine(toolsWeb, name);
            var b = Path.Combine(extWeb, name);
            Assert.True(File.Exists(a), $"missing: {a}");
            Assert.True(File.Exists(b), $"missing: {b}");
            Assert.Equal(Sha256(a), Sha256(b));
        }
    }

    private static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(path)));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LineOpenApi.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
