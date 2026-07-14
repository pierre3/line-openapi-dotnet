using System.Text.Json;
using System.Text.Json.Serialization;

namespace Line.OpenApi.Cli.Output;

/// <summary>Renders DTOs as stable, indented JSON for <c>--json</c> output and MCP tool results.</summary>
internal static class Json
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Serializes a value to JSON.</summary>
    public static string Serialize(object value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Writes a value as JSON to stdout.</summary>
    public static void Print(object value) => Console.WriteLine(Serialize(value));
}
