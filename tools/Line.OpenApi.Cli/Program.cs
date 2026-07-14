using Line.OpenApi.Cli.Hosting;

namespace Line.OpenApi.Cli;

/// <summary>
/// Entry point. Dispatches between two execution modes that share the same
/// service layer (spec §2):
/// <list type="bullet">
///   <item><description>Default: CLI mode driven by Cocona (<c>line &lt;command&gt; ...</c>).</description></item>
///   <item><description><c>line mcp</c>: MCP server over stdio for AI agents.</description></item>
/// </list>
/// </summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // MCP server mode: `line mcp [...]`. Everything after "mcp" is forwarded.
        if (args.Length > 0 && string.Equals(args[0], "mcp", StringComparison.OrdinalIgnoreCase))
        {
            return await McpServerHost.RunAsync(args[1..]).ConfigureAwait(false);
        }

        // Default: CLI mode (Cocona).
        return await CliHost.RunAsync(args).ConfigureAwait(false);
    }
}
