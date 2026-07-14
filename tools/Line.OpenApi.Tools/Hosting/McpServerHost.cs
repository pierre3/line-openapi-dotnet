using Line.OpenApi.Tools.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Line.OpenApi.Tools.Hosting;

/// <summary>
/// Runs the MCP server over stdio. Read-only tools are always registered; mutating tools are
/// registered only when <c>--read-only</c> is absent. Tools are discovered via the
/// <c>[McpServerToolType]</c>/<c>[McpServerTool]</c> attributes (see <c>Mcp/</c>).
/// </summary>
internal static class McpServerHost
{
    public static async Task<int> RunAsync(string[] args)
    {
        var readOnly = HasFlag(args, "--read-only");
        var allowSecretOutput = HasFlag(args, "--allow-secret-output");
        var allowRemoteReplay = HasFlag(args, "--allow-remote-replay");

        var builder = Host.CreateApplicationBuilder(args);

        // stdio transport uses stdout for the protocol stream, so all logging must
        // go to stderr; otherwise log lines corrupt the JSON-RPC framing.
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services.AddCliCoreServices();
        builder.Services.AddSingleton(new McpToolOptions(readOnly, allowSecretOutput, allowRemoteReplay));

        var mcp = builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<ReadTools>();

        if (!readOnly)
        {
            mcp.WithTools<WriteTools>();
        }

        await builder.Build().RunAsync().ConfigureAwait(false);
        return 0;
    }

    private static bool HasFlag(string[] args, string flag) =>
        Array.Exists(args, a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));
}
