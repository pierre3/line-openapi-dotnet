using System.Text.Json;
using System.Text.Json.Nodes;
using Line.OpenApi.Tools.Services;
using Xunit;

namespace Line.OpenApi.Tools.Tests;

/// <summary>
/// Verifies that <see cref="MessageSchemaService"/> extracts a self-contained JSON Schema
/// document from the embedded spec: the closure is complete (no dangling refs), references are
/// rewritten into <c>$defs</c>, discriminators/externalDocs survive, and the self-recursive
/// FlexBox does not blow up.
/// </summary>
public sealed class MessageSchemaServiceTests
{
    private static readonly MessageSchemaService Service = new();

    private static JsonObject Parse(string type)
    {
        var json = Service.GetSchema(type);
        return (JsonObject)JsonNode.Parse(json)!;
    }

    private static IEnumerable<string> RefTargets(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var kvp in obj)
                {
                    if (kvp.Key == "$ref" && kvp.Value is JsonValue v && v.TryGetValue<string>(out var s))
                    {
                        yield return s;
                    }
                    else
                    {
                        foreach (var r in RefTargets(kvp.Value)) yield return r;
                    }
                }
                break;
            case JsonArray arr:
                foreach (var item in arr)
                {
                    foreach (var r in RefTargets(item)) yield return r;
                }
                break;
        }
    }

    [Theory]
    [InlineData("flex", "FlexContainer")]
    [InlineData("template", "Template")]
    [InlineData("all", "Message")]
    [InlineData("imagemap", "ImagemapMessage")]
    [InlineData("quickReply", "QuickReply")]
    [InlineData("action", "Action")]
    public void GetSchema_returns_document_rooted_at_the_expected_schema(string type, string root)
    {
        var doc = Parse(type);

        Assert.Equal($"#/$defs/{root}", doc["$ref"]!.GetValue<string>());
        var defs = Assert.IsType<JsonObject>(doc["$defs"]);
        Assert.True(defs.ContainsKey(root), $"$defs must contain the root schema '{root}'.");
    }

    [Fact]
    public void GetSchema_defaults_to_flex()
    {
        var doc = (JsonObject)JsonNode.Parse(Service.GetSchema())!;
        Assert.Equal("#/$defs/FlexContainer", doc["$ref"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("flex")]
    [InlineData("template")]
    [InlineData("all")]
    public void GetSchema_closure_is_complete_with_no_dangling_refs(string type)
    {
        var doc = Parse(type);
        var defs = (JsonObject)doc["$defs"]!;

        // Every $ref must point into $defs, and every target must actually exist there.
        foreach (var target in RefTargets(doc))
        {
            Assert.StartsWith("#/$defs/", target);
            var name = target["#/$defs/".Length..];
            Assert.True(defs.ContainsKey(name), $"Dangling reference: '{name}' is not present in $defs.");
        }
    }

    [Fact]
    public void GetSchema_rewrites_all_component_refs_into_defs()
    {
        // No original OpenAPI-style pointer must leak through (covers both $ref and discriminator mapping).
        Assert.DoesNotContain("#/components/schemas/", Service.GetSchema("all"));
    }

    [Fact]
    public void GetSchema_flex_includes_the_self_recursive_FlexBox_closure()
    {
        var defs = (JsonObject)Parse("flex")["$defs"]!;

        // FlexBox → FlexComponent → FlexBox recursion must be represented by references, not
        // infinite inlining; the mere fact that extraction terminated proves the visited-set guard,
        // and all three participants must be present.
        Assert.True(defs.ContainsKey("FlexBox"));
        Assert.True(defs.ContainsKey("FlexComponent"));
        Assert.True(defs.ContainsKey("FlexBubble"));
    }

    [Fact]
    public void GetSchema_preserves_discriminator_mapping_rewritten_into_defs()
    {
        var defs = (JsonObject)Parse("all")["$defs"]!;
        var message = (JsonObject)defs["Message"]!;
        var mapping = (JsonObject)((JsonObject)message["discriminator"]!)["mapping"]!;

        Assert.Equal("#/$defs/TextMessage", mapping["text"]!.GetValue<string>());
        Assert.Equal("#/$defs/FlexMessage", mapping["flex"]!.GetValue<string>());
        Assert.True(defs.ContainsKey("TextMessage"));
        Assert.True(defs.ContainsKey("FlexMessage"));
    }

    [Fact]
    public void GetSchema_preserves_externalDocs_links()
    {
        // externalDocs URLs are handy hints to the official LINE docs; they must survive extraction.
        Assert.Contains("externalDocs", Service.GetSchema("all"));
        Assert.Contains("developers.line.biz", Service.GetSchema("all"));
    }

    [Fact]
    public void GetSchema_is_valid_json_with_a_draft_marker()
    {
        var doc = Parse("flex");
        Assert.Contains("json-schema.org", doc["$schema"]!.GetValue<string>());
    }

    [Fact]
    public void GetSchema_throws_on_unknown_type()
    {
        var ex = Assert.Throws<MessageInputException>(() => Service.GetSchema("nope"));
        Assert.Contains("Unknown schema type", ex.Message);
    }
}
