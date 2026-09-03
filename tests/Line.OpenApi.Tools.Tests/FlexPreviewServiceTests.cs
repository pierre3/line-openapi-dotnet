using System.Text.Json.Nodes;
using Line.OpenApi.Tools.Services;
using Xunit;

namespace Line.OpenApi.Tools.Tests;

/// <summary>
/// Unit tests for the pure validation / normalization / persistence logic of
/// <see cref="FlexPreviewService"/>. These paths do not start the HTTP listener
/// or open a browser, so they run fast and offline.
/// </summary>
public sealed class FlexPreviewServiceTests
{
    private static FlexPreviewService NewService()
    {
        // Never spawn a browser from a test run.
        Environment.SetEnvironmentVariable("LINE_FLEX_MCP_NO_OPEN", "1");
        return new FlexPreviewService();
    }

    private static string Bubble(string block = "\"body\":{\"type\":\"box\",\"layout\":\"vertical\",\"contents\":[{\"type\":\"text\",\"text\":\"hi\"}]}")
        => "{\"type\":\"bubble\"," + block + "}";

    private static string Carousel(int bubbles)
    {
        var items = string.Join(",", Enumerable.Repeat(Bubble(), bubbles));
        return "{\"type\":\"carousel\",\"contents\":[" + items + "]}";
    }

    [Fact]
    public void Bare_bubble_with_a_block_is_valid()
    {
        var (valid, warnings) = NewService().ValidateInput(Bubble());
        Assert.True(valid);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Bubble_without_any_block_warns()
    {
        var (valid, warnings) = NewService().ValidateInput("{\"type\":\"bubble\"}");
        Assert.False(valid);
        Assert.Contains(warnings, w => w.Contains("header/hero/body/footer"));
    }

    [Fact]
    public void Flex_message_wrapper_is_unwrapped_and_validated()
    {
        var msg = "{\"type\":\"flex\",\"altText\":\"x\",\"contents\":" + Bubble() + "}";
        var (valid, warnings) = NewService().ValidateInput(msg);
        Assert.True(valid);
        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    public void Carousel_within_bounds_is_valid(int count)
    {
        var (valid, warnings) = NewService().ValidateInput(Carousel(count));
        Assert.True(valid);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Empty_carousel_is_invalid()
    {
        var (valid, warnings) = NewService().ValidateInput("{\"type\":\"carousel\",\"contents\":[]}");
        Assert.False(valid);
        Assert.Contains(warnings, w => w.Contains("empty"));
    }

    [Fact]
    public void Carousel_over_twelve_bubbles_is_invalid()
    {
        var (valid, warnings) = NewService().ValidateInput(Carousel(13));
        Assert.False(valid);
        Assert.Contains(warnings, w => w.Contains("12"));
    }

    [Fact]
    public void Non_bubble_entry_in_carousel_is_reported()
    {
        var payload = "{\"type\":\"carousel\",\"contents\":[{\"type\":\"box\",\"layout\":\"vertical\",\"contents\":[]}]}";
        var (valid, warnings) = NewService().ValidateInput(payload);
        Assert.False(valid);
        Assert.Contains(warnings, w => w.Contains("is not a bubble"));
    }

    [Fact]
    public void Arbitrary_object_is_not_a_container()
    {
        var (valid, warnings) = NewService().ValidateInput("{\"foo\":1}");
        Assert.False(valid);
        Assert.Contains(warnings, w => w.Contains("container"));
    }

    [Fact]
    public void Malformed_json_is_reported_structurally_not_thrown()
    {
        // The validate tool must never throw on bad input; it returns { valid:false, warnings }.
        var (valid, warnings) = NewService().ValidateInput("{ not json");
        Assert.False(valid);
        Assert.NotEmpty(warnings);
    }

    [Fact]
    public void Scalar_json_is_reported_structurally_not_thrown()
    {
        var (valid, warnings) = NewService().ValidateInput("123");
        Assert.False(valid);
        Assert.NotEmpty(warnings);
    }

    [Fact]
    public void Null_input_with_no_stored_content_reports_nothing_to_validate()
    {
        var stateDir = Path.Combine(Path.GetTempPath(), "line-flex-mcp-tests", Guid.NewGuid().ToString("N"));
        var prev = Environment.GetEnvironmentVariable("LINE_FLEX_MCP_STATE_DIR");
        try
        {
            Environment.SetEnvironmentVariable("LINE_FLEX_MCP_STATE_DIR", stateDir);
            var (valid, warnings) = NewService().ValidateInput(null);
            Assert.False(valid);
            Assert.Contains(warnings, w => w.Contains("no content"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LINE_FLEX_MCP_STATE_DIR", prev);
        }
    }

    [Fact]
    public void GetContent_unwraps_the_persisted_state_wrapper()
    {
        var stateDir = Path.Combine(Path.GetTempPath(), "line-flex-mcp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stateDir);
        var bubble = Bubble();
        File.WriteAllText(Path.Combine(stateDir, "content.json"),
            "{\"content\":" + bubble + "}");

        var prev = Environment.GetEnvironmentVariable("LINE_FLEX_MCP_STATE_DIR");
        try
        {
            Environment.SetEnvironmentVariable("LINE_FLEX_MCP_STATE_DIR", stateDir);
            var content = NewService().GetContent();
            Assert.NotNull(content);
            Assert.Equal("bubble", (string?)content!["type"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LINE_FLEX_MCP_STATE_DIR", prev);
        }
    }
}
