using System.ComponentModel;
using System.Reflection;
using Line.OpenApi.Tools.Mcp;
using ModelContextProtocol.Server;
using Xunit;

namespace Line.OpenApi.Tools.Tests;

/// <summary>
/// Guards the read-only safety property: the tools registered under <c>--read-only</c> (ReadTools)
/// are exactly the non-mutating ones, and all tool names follow <c>line_&lt;area&gt;_&lt;verb&gt;</c>.
/// </summary>
public sealed class McpToolRegistrationTests
{
    private static IEnumerable<MethodInfo> ToolMethods(Type toolType) =>
        toolType.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null);

    private static IEnumerable<string> ToolNames(Type toolType) =>
        ToolMethods(toolType).Select(m => m.GetCustomAttribute<McpServerToolAttribute>()!.Name!);

    [Fact]
    public void Tool_surface_is_exactly_the_expected_set()
    {
        // Surface snapshot: adding/renaming/removing a tool must update this set intentionally.
        var expected = new[]
        {
            "line_ping",
            "line_bot_info", "line_bot_profile", "line_bot_quota", "line_bot_quota_consumption",
            "line_liff_list", "line_token_verify", "line_webhook_verify",
            "line_message_push", "line_message_multicast", "line_message_broadcast", "line_message_reply",
            "line_liff_add", "line_liff_update", "line_liff_delete",
            "line_token_issue", "line_token_revoke", "line_webhook_replay",
        }.OrderBy(n => n).ToArray();

        var actual = ToolNames(typeof(ReadTools)).Concat(ToolNames(typeof(WriteTools))).OrderBy(n => n).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Every_tool_has_a_non_empty_description()
    {
        // The [Description] is the LLM-facing contract; it must never be blank.
        var methods = ToolMethods(typeof(ReadTools)).Concat(ToolMethods(typeof(WriteTools)));
        Assert.All(methods, m =>
        {
            var description = m.GetCustomAttribute<DescriptionAttribute>()?.Description;
            Assert.False(string.IsNullOrWhiteSpace(description), $"{m.Name} is missing a [Description].");
        });
    }

    [Fact]
    public void All_tool_names_use_line_prefix()
    {
        var all = ToolNames(typeof(ReadTools)).Concat(ToolNames(typeof(WriteTools)));
        Assert.All(all, name => Assert.StartsWith("line_", name));
    }

    [Fact]
    public void ReadTools_contains_only_non_mutating_operations()
    {
        var readOnly = ToolNames(typeof(ReadTools)).ToHashSet();

        Assert.Contains("line_bot_info", readOnly);
        Assert.Contains("line_liff_list", readOnly);
        Assert.Contains("line_token_verify", readOnly);
        Assert.Contains("line_webhook_verify", readOnly);

        // Mutating operations must never appear in the read-only set.
        Assert.DoesNotContain("line_message_push", readOnly);
        Assert.DoesNotContain("line_liff_delete", readOnly);
        Assert.DoesNotContain("line_token_issue", readOnly);
        Assert.DoesNotContain("line_token_revoke", readOnly);
        Assert.DoesNotContain("line_webhook_replay", readOnly);
    }

    [Fact]
    public void WriteTools_contains_the_mutating_operations()
    {
        var write = ToolNames(typeof(WriteTools)).ToHashSet();

        Assert.Contains("line_message_push", write);
        Assert.Contains("line_message_broadcast", write);
        Assert.Contains("line_liff_add", write);
        Assert.Contains("line_token_issue", write);
        Assert.Contains("line_webhook_replay", write);
    }

    [Fact]
    public void Read_and_write_tool_sets_are_disjoint()
    {
        var readOnly = ToolNames(typeof(ReadTools)).ToHashSet();
        var write = ToolNames(typeof(WriteTools)).ToHashSet();
        Assert.Empty(readOnly.Intersect(write));
    }
}
