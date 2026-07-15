using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using SharpYaml;
using SharpYaml.Serialization;

namespace Line.OpenApi.Tools.Services;

/// <summary>
/// Extracts JSON Schema for LINE message objects from the embedded <c>messaging-api.yml</c>
/// (the same spec Kiota generates from, so the schema never drifts from the models).
/// <para>
/// For a requested root (e.g. <c>FlexContainer</c>) it returns the transitive closure of
/// <c>$ref</c>-reachable schemas as a single self-contained document (root <c>$ref</c> plus a
/// <c>$defs</c> map). Schemas are kept as references rather than inlined, which is mandatory
/// because <c>FlexBox</c> is self-recursive (<c>FlexBox → FlexComponent → FlexBox</c>) and would
/// otherwise expand forever. <c>discriminator</c>/<c>mapping</c> and <c>externalDocs</c> are
/// preserved verbatim so the model can pick concrete subtypes and follow the official docs.
/// </para>
/// </summary>
public sealed class MessageSchemaService
{
    // Embedded via <EmbeddedResource LogicalName="..."> in the csproj (points at the repo's
    // canonical openapi/messaging-api.yml so there is a single source of truth).
    private const string ResourceName = "Line.OpenApi.Tools.messaging-api.yml";

    private const string ComponentsPrefix = "#/components/schemas/";
    private const string DefsPrefix = "#/$defs/";

    /// <summary>Maps the user-facing <c>type</c> argument to the OpenAPI schema name used as the root.</summary>
    private static readonly IReadOnlyDictionary<string, string> Roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["all"] = "Message",
        ["flex"] = "FlexContainer",
        ["template"] = "Template",
        ["imagemap"] = "ImagemapMessage",
        ["quickReply"] = "QuickReply",
        ["action"] = "Action",
        // Rich menu roots (same spec), surfaced via line_richmenu_schema.
        ["richmenu"] = "RichMenuRequest",
        ["richMenuAlias"] = "CreateRichMenuAliasRequest",
    };

    // Relaxed escaping keeps the output readable for the model (backticks, quotes, angle brackets
    // stay literal). The result is JSON tool output, not HTML, so this is safe.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Parsed once and cached (the service is a singleton): the spec is ~190 KB of YAML.
    private readonly Lazy<JsonObject> _schemas = new(LoadSchemas, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The accepted values for the <c>type</c> argument.</summary>
    public static IReadOnlyCollection<string> SchemaTypes => Roots.Keys.ToArray();

    /// <summary>
    /// Returns the JSON Schema document for the given subtree. <paramref name="type"/> is one of
    /// <see cref="SchemaTypes"/> (default <c>flex</c>).
    /// </summary>
    public string GetSchema(string? type = "flex")
    {
        var key = string.IsNullOrWhiteSpace(type) ? "flex" : type.Trim();
        if (!Roots.TryGetValue(key, out var root))
        {
            throw new MessageInputException(
                $"Unknown schema type '{type}'. Valid values: {string.Join(", ", Roots.Keys)}.");
        }

        var schemas = _schemas.Value;
        var defs = new JsonObject();
        var visited = new HashSet<string>(StringComparer.Ordinal) { root };
        var queue = new Queue<string>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            if (!schemas.TryGetPropertyValue(name, out var schemaNode) || schemaNode is null)
            {
                // Dangling target: skip. The spec is self-contained so this is defensive only.
                continue;
            }

            var clone = schemaNode.DeepClone();
            RewriteAndCollect(clone, target =>
            {
                if (visited.Add(target))
                {
                    queue.Enqueue(target);
                }
            });
            defs[name] = clone;
        }

        var document = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["title"] = root,
            ["$comment"] = "Extracted from the LINE messaging-api OpenAPI spec. Objects are polymorphic: "
                + "choose a concrete subtype via its `type` value (see each schema's `discriminator.mapping`). "
                + "`externalDocs.url`, when present, links to the official LINE documentation.",
            ["$ref"] = DefsPrefix + root,
            ["$defs"] = defs,
        };
        return document.ToJsonString(SerializerOptions);
    }

    /// <summary>
    /// Walks a schema node tree, rewriting every <c>#/components/schemas/X</c> reference (both
    /// <c>$ref</c> values and <c>discriminator.mapping</c> targets) to <c>#/$defs/X</c> and reporting
    /// each referenced name via <paramref name="onRef"/> so the caller can expand the closure.
    /// </summary>
    private static void RewriteAndCollect(JsonNode? node, Action<string> onRef)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var propertyKey in obj.Select(kvp => kvp.Key).ToList())
                {
                    var child = obj[propertyKey];
                    if (TryReadRef(child, out var name))
                    {
                        obj[propertyKey] = DefsPrefix + name;
                        onRef(name);
                    }
                    else
                    {
                        RewriteAndCollect(child, onRef);
                    }
                }
                break;

            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    var child = arr[i];
                    if (TryReadRef(child, out var name))
                    {
                        arr[i] = DefsPrefix + name;
                        onRef(name);
                    }
                    else
                    {
                        RewriteAndCollect(child, onRef);
                    }
                }
                break;
        }
    }

    private static bool TryReadRef(JsonNode? node, out string name)
    {
        name = string.Empty;
        if (node is JsonValue value
            && value.TryGetValue<string>(out var text)
            && text is not null
            && text.StartsWith(ComponentsPrefix, StringComparison.Ordinal))
        {
            name = text[ComponentsPrefix.Length..];
            return true;
        }
        return false;
    }

    private static JsonObject LoadSchemas()
    {
        using var stream = typeof(MessageSchemaService).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);

        var yaml = new YamlStream();
        yaml.Load(reader);
        var root = (YamlMappingNode)yaml.Documents[0].RootNode;
        var components = (YamlMappingNode)GetChild(root, "components");
        var schemas = (YamlMappingNode)GetChild(components, "schemas");
        return (JsonObject)ToJson(schemas)!;
    }

    private static YamlNode GetChild(YamlMappingNode map, string key)
    {
        foreach (var entry in map.Children)
        {
            if (entry.Key is YamlScalarNode scalar && scalar.Value == key)
            {
                return entry.Value;
            }
        }
        throw new InvalidOperationException($"Expected key '{key}' in the OpenAPI document.");
    }

    private static JsonNode? ToJson(YamlNode node) => node switch
    {
        YamlMappingNode map => MapToObject(map),
        YamlSequenceNode seq => SeqToArray(seq),
        YamlScalarNode scalar => ScalarToJson(scalar),
        _ => null,
    };

    private static JsonObject MapToObject(YamlMappingNode map)
    {
        var obj = new JsonObject();
        foreach (var entry in map.Children)
        {
            var key = ((YamlScalarNode)entry.Key).Value!;
            obj[key] = ToJson(entry.Value);
        }
        return obj;
    }

    private static JsonArray SeqToArray(YamlSequenceNode seq)
    {
        var arr = new JsonArray();
        foreach (var item in seq.Children)
        {
            arr.Add(ToJson(item));
        }
        return arr;
    }

    private static JsonNode? ScalarToJson(YamlScalarNode scalar)
    {
        var value = scalar.Value;
        // Quoted/literal/folded scalars are always strings; only plain scalars carry an implicit type.
        if (scalar.Style != ScalarStyle.Plain || value is null)
        {
            return value is null ? null : JsonValue.Create(value);
        }

        switch (value)
        {
            case "":
                return JsonValue.Create(string.Empty);
            case "null" or "Null" or "NULL" or "~":
                return null;
            case "true" or "True" or "TRUE":
                return JsonValue.Create(true);
            case "false" or "False" or "FALSE":
                return JsonValue.Create(false);
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return JsonValue.Create(integer);
        }
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return JsonValue.Create(number);
        }
        return JsonValue.Create(value);
    }
}
