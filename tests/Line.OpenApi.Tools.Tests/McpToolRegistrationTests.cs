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
            "line_ping", "line_message_schema",
            "line_bot_info", "line_bot_profile", "line_bot_quota", "line_bot_quota_consumption",
            "line_liff_list", "line_token_verify", "line_webhook_verify",
            "line_webhook_get_endpoint", "line_webhook_test_endpoint",
            "line_message_push", "line_message_multicast", "line_message_broadcast", "line_message_reply",
            "line_liff_add", "line_liff_update", "line_liff_update_url", "line_liff_delete",
            "line_token_issue", "line_token_revoke", "line_webhook_replay", "line_webhook_set_endpoint",
            // Rich menu
            "line_richmenu_schema", "line_richmenu_list", "line_richmenu_get",
            "line_richmenu_get_default", "line_richmenu_id_of_user",
            "line_richmenu_create", "line_richmenu_delete", "line_richmenu_set_default",
            "line_richmenu_cancel_default", "line_richmenu_link", "line_richmenu_unlink",
            // Insight (all read-only)
            "line_insight_demographic", "line_insight_deliveries", "line_insight_followers",
            "line_insight_events", "line_insight_per_unit", "line_insight_richmenu_summary",
            "line_insight_richmenu_daily",
            // Manage Audience (list/get read-only; create/add_users/delete mutating; by-file is CLI-only)
            "line_audience_list", "line_audience_get",
            "line_audience_create", "line_audience_add_users", "line_audience_delete",
            // Shop
            "line_shop_mission",
            // Flex preview (read-only-safe: no LINE API / secrets; registered unconditionally)
            "line_flex_preview", "line_flex_get_content", "line_flex_validate", "line_flex_open",
        }.OrderBy(n => n).ToArray();

        var actual = ToolNames(typeof(ReadTools))
            .Concat(ToolNames(typeof(WriteTools)))
            .Concat(ToolNames(typeof(FlexPreviewTools)))
            .OrderBy(n => n).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Every_tool_has_a_non_empty_description()
    {
        // The [Description] is the LLM-facing contract; it must never be blank.
        var methods = ToolMethods(typeof(ReadTools))
            .Concat(ToolMethods(typeof(WriteTools)))
            .Concat(ToolMethods(typeof(FlexPreviewTools)));
        Assert.All(methods, m =>
        {
            var description = m.GetCustomAttribute<DescriptionAttribute>()?.Description;
            Assert.False(string.IsNullOrWhiteSpace(description), $"{m.Name} is missing a [Description].");
        });
    }

    [Fact]
    public void All_tool_names_use_line_prefix()
    {
        var all = ToolNames(typeof(ReadTools))
            .Concat(ToolNames(typeof(WriteTools)))
            .Concat(ToolNames(typeof(FlexPreviewTools)));
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
        Assert.Contains("line_message_schema", readOnly);
        // Webhook endpoint get/test are non-mutating diagnostics (test only asks LINE to probe the URL).
        Assert.Contains("line_webhook_get_endpoint", readOnly);
        Assert.Contains("line_webhook_test_endpoint", readOnly);
        // Insight is entirely read-only. Assert ALL seven so misclassifying one into WriteTools —
        // which is a hidden availability regression under --read-only, not a safety one, and so is
        // caught by neither the exact-set nor the disjoint test — fails here.
        string[] insightTools =
        {
            "line_insight_demographic", "line_insight_deliveries", "line_insight_followers",
            "line_insight_events", "line_insight_per_unit", "line_insight_richmenu_summary",
            "line_insight_richmenu_daily",
        };
        Assert.All(insightTools, name => Assert.Contains(name, readOnly));
        // Audience list/get are safe reads.
        Assert.Contains("line_audience_list", readOnly);
        Assert.Contains("line_audience_get", readOnly);

        // Mutating operations must never appear in the read-only set.
        Assert.DoesNotContain("line_message_push", readOnly);
        Assert.DoesNotContain("line_liff_delete", readOnly);
        Assert.DoesNotContain("line_liff_update_url", readOnly);
        Assert.DoesNotContain("line_token_issue", readOnly);
        Assert.DoesNotContain("line_token_revoke", readOnly);
        Assert.DoesNotContain("line_webhook_replay", readOnly);
        Assert.DoesNotContain("line_webhook_set_endpoint", readOnly);
        // Rich menu mutations (set_default changes what every user sees) must stay out of read-only.
        Assert.DoesNotContain("line_richmenu_create", readOnly);
        Assert.DoesNotContain("line_richmenu_delete", readOnly);
        Assert.DoesNotContain("line_richmenu_set_default", readOnly);
        Assert.DoesNotContain("line_richmenu_link", readOnly);
        Assert.DoesNotContain("line_richmenu_unlink", readOnly);
        // Audience/shop mutations must stay out of read-only.
        Assert.DoesNotContain("line_audience_create", readOnly);
        Assert.DoesNotContain("line_audience_add_users", readOnly);
        Assert.DoesNotContain("line_audience_delete", readOnly);
        Assert.DoesNotContain("line_shop_mission", readOnly);
    }

    [Fact]
    public void WriteTools_contains_the_mutating_operations()
    {
        var write = ToolNames(typeof(WriteTools)).ToHashSet();

        Assert.Contains("line_message_push", write);
        Assert.Contains("line_message_broadcast", write);
        Assert.Contains("line_liff_add", write);
        Assert.Contains("line_liff_update_url", write);
        Assert.Contains("line_token_issue", write);
        Assert.Contains("line_webhook_replay", write);
        Assert.Contains("line_webhook_set_endpoint", write);

        // Flex preview tools touch no LINE API / secrets and must stay available under --read-only,
        // so they must never be classified as mutating.
        Assert.DoesNotContain("line_flex_preview", write);
        Assert.DoesNotContain("line_flex_get_content", write);
        Assert.DoesNotContain("line_flex_validate", write);
        Assert.DoesNotContain("line_flex_open", write);
    }

    [Fact]
    public void Read_and_write_tool_sets_are_disjoint()
    {
        var readOnly = ToolNames(typeof(ReadTools)).ToHashSet();
        var write = ToolNames(typeof(WriteTools)).ToHashSet();
        Assert.Empty(readOnly.Intersect(write));
    }
}
