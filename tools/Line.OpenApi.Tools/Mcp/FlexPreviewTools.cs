using System.ComponentModel;
using Line.OpenApi.Tools.Output;
using Line.OpenApi.Tools.Services;
using ModelContextProtocol.Server;

namespace Line.OpenApi.Tools.Mcp;

/// <summary>
/// Read-only MCP tools for previewing a LINE Flex Message in a live local browser view.
///
/// These tools spin up a loopback-only web server that renders the Flex JSON exactly as
/// the LINE app would (using the same renderer as the Copilot canvas / standalone viewer),
/// open the default browser once, and push subsequent updates to the open tab. They call no
/// LINE API and return no secrets, so they are safe under <c>--read-only</c>. Tool names
/// follow <c>line_&lt;area&gt;_&lt;verb&gt;</c> (spec §4.5).
/// </summary>
[McpServerToolType]
internal class FlexPreviewTools
{
    [McpServerTool(Name = "line_flex_preview"), Description(
        "Render a LINE Flex Message in a live local browser preview and return its URL. "
        + "Pass the Flex JSON as a string: either a full flex message object "
        + "(type:\"flex\", altText, contents) or a bare container (type:\"bubble\"/\"carousel\"). "
        + "The preview renders like the LINE app, opens the browser on first call, and is "
        + "hot-updated on later calls. Returns { ok, url, valid, warnings, opened }.")]
    public static string Preview(
        FlexPreviewService preview,
        [Description("The Flex Message JSON as a string (a flex message object or a bubble/carousel container).")]
        string contentJson,
        [Description("Optional altText to record with the message (informational; not required for rendering).")]
        string? altText = null)
    {
        var result = preview.Preview(contentJson, altText);
        return Json.Serialize(new
        {
            ok = result.Ok,
            url = result.Url,
            valid = result.Valid,
            warnings = result.Warnings,
            opened = result.Opened,
        });
    }

    [McpServerTool(Name = "line_flex_get_content"), Description(
        "Get the Flex JSON currently shown in the preview, including any edits the user made "
        + "in the browser. Use this to pick up manual adjustments before saving or sending. "
        + "Returns { content } (null when nothing has been previewed yet).")]
    public static string GetContent(FlexPreviewService preview)
        => Json.Serialize(new { content = preview.GetContent() });

    [McpServerTool(Name = "line_flex_validate"), Description(
        "Structurally validate Flex JSON (container shape, carousel size, per-bubble blocks) "
        + "without rendering. Pass contentJson to validate that, or omit it to validate the "
        + "current preview content. Returns { valid, warnings }.")]
    public static string Validate(
        FlexPreviewService preview,
        [Description("Optional Flex JSON string to validate. Omit to validate the current preview content.")]
        string? contentJson = null)
    {
        var (valid, warnings) = preview.ValidateInput(contentJson);
        return Json.Serialize(new { valid, warnings });
    }

    [McpServerTool(Name = "line_flex_open"), Description(
        "Ensure the preview server is running and (re)open it in the browser. Useful when the "
        + "tab was closed. Returns { ok, url }.")]
    public static string Open(FlexPreviewService preview)
        => Json.Serialize(new { ok = true, url = preview.Open() });
}
