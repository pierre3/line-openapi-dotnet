using System.Reflection;
using Cocona;

namespace Line.OpenApi.Cli.Cli;

/// <summary>Diagnostic commands (version).</summary>
internal sealed class DiagnosticsCommands
{
    [Command("version", Description = "Print the tool version.")]
    public void Version()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";
        Console.WriteLine($"line {version}");
    }
}
