using System.ComponentModel;
using Line.OpenApi.Tools.Configuration;
using Line.OpenApi.Tools.Output;
using Line.OpenApi.Tools.Services;
using ModelContextProtocol.Server;

namespace Line.OpenApi.Tools.Mcp;

/// <summary>
/// Read-only MCP tools (safe under <c>--read-only</c>): bot lookup, LIFF listing, rich menu
/// reads, insight statistics, audience listing/get, token verify, webhook verify. Results are
/// returned as JSON text and contain no secrets. Tool names follow
/// <c>line_&lt;area&gt;_&lt;verb&gt;</c> (spec §4.5).
/// </summary>
[McpServerToolType]
internal class ReadTools
{
    [McpServerTool(Name = "line_ping"), Description("Health check that returns \"pong\".")]
    public static string Ping() => "pong";

    [McpServerTool(Name = "line_message_schema"), Description(
        "Returns the JSON Schema for LINE message objects so you can build a valid messagesJson array. "
        + "Call this BEFORE constructing flex or template messages — they are large and self-recursive. "
        + "Simple messages (text/image/video/audio/location/sticker) have trivial shapes already shown in "
        + "the send-tool descriptions and usually do not need this. Tip: after building a message, call a "
        + "send tool with dryRun=true to parse and shape-check it (not full schema validation) before "
        + "actually sending.")]
    public static string MessageSchema(
        MessageSchemaService schema,
        [Description("Which subtree to return: all | flex | template | imagemap | quickReply | action. Default: flex.")]
        string type = "flex")
        => schema.GetSchema(type);

    [McpServerTool(Name = "line_richmenu_schema"), Description(
        "Returns the JSON Schema for a LINE rich menu object, so you can build a valid rich menu "
        + "definition for line_richmenu_create. type is 'richmenu' (the RichMenuRequest, default) or "
        + "'richMenuAlias'. Read-only, returns no secrets. Note: after creating a rich menu you must "
        + "upload its image with the CLI (`line richmenu image <id> --file menu.png`) — image upload is "
        + "not available over MCP.")]
    public static string RichMenuSchema(
        MessageSchemaService schema,
        [Description("Which subtree: richmenu | richMenuAlias. Default: richmenu.")] string type = "richmenu")
        => schema.GetSchema(type);

    [McpServerTool(Name = "line_richmenu_list"), Description("List the channel's rich menus (id, name, chat bar text, area count, default flag).")]
    public static async Task<string> RichMenuList(
        RichMenuService richMenu, CredentialResolver resolver,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        var menus = await richMenu.ListAsync(Resolve(resolver, profile), CancellationToken.None);
        return Json.Serialize(menus);
    }

    [McpServerTool(Name = "line_richmenu_get"), Description("Get a rich menu by id.")]
    public static async Task<string> RichMenuGet(
        RichMenuService richMenu, CredentialResolver resolver,
        [Description("Rich menu id.")] string richMenuId,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        var menu = await richMenu.GetAsync(Resolve(resolver, profile), richMenuId, CancellationToken.None);
        return menu is null ? Json.Serialize(new { richMenuId, found = false }) : Json.Serialize(menu);
    }

    [McpServerTool(Name = "line_richmenu_get_default"), Description("Get the default rich menu id (null if none is set).")]
    public static async Task<string> RichMenuGetDefault(
        RichMenuService richMenu, CredentialResolver resolver,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        var id = await richMenu.GetDefaultIdAsync(Resolve(resolver, profile), CancellationToken.None);
        return Json.Serialize(new { richMenuId = id });
    }

    [McpServerTool(Name = "line_richmenu_id_of_user"), Description("Get the rich menu id linked to a user (null if none).")]
    public static async Task<string> RichMenuIdOfUser(
        RichMenuService richMenu, CredentialResolver resolver,
        [Description("Target user id.")] string userId,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        var id = await richMenu.GetIdOfUserAsync(Resolve(resolver, profile), userId, CancellationToken.None);
        return Json.Serialize(new { richMenuId = id });
    }

    [McpServerTool(Name = "line_bot_info"), Description("Get LINE bot information (userId, basicId, displayName, chat mode).")]
    public static async Task<string> BotInfo(
        MessageService messages, CredentialResolver resolver,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        var info = await messages.GetBotInfoAsync(Resolve(resolver, profile), CancellationToken.None);
        return Json.Serialize(info);
    }

    [McpServerTool(Name = "line_bot_quota"), Description("Get the monthly message quota limit.")]
    public static async Task<string> BotQuota(
        MessageService messages, CredentialResolver resolver,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        var quota = await messages.GetQuotaAsync(Resolve(resolver, profile), CancellationToken.None);
        return Json.Serialize(quota);
    }

    [McpServerTool(Name = "line_bot_quota_consumption"), Description("Get the current month's message consumption count.")]
    public static async Task<string> BotQuotaConsumption(
        MessageService messages, CredentialResolver resolver,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        var used = await messages.GetQuotaConsumptionAsync(Resolve(resolver, profile), CancellationToken.None);
        return Json.Serialize(new { totalUsage = used });
    }

    [McpServerTool(Name = "line_bot_profile"), Description("Get a user's LINE profile by user id.")]
    public static async Task<string> BotProfile(
        MessageService messages, CredentialResolver resolver,
        [Description("Target user id.")] string userId,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        var result = await messages.GetProfileAsync(Resolve(resolver, profile), userId, CancellationToken.None);
        return Json.Serialize(result);
    }

    [McpServerTool(Name = "line_liff_list"), Description("List the channel's registered LIFF apps.")]
    public static async Task<string> LiffList(
        LiffService liff, CredentialResolver resolver,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        var apps = await liff.ListAsync(Resolve(resolver, profile), CancellationToken.None);
        return Json.Serialize(apps);
    }

    [McpServerTool(Name = "line_insight_demographic"), Description("Get the demographic attributes (gender/age/area/etc.) of the bot's friends.")]
    public static async Task<string> InsightDemographic(
        InsightService insight, CredentialResolver resolver,
        [Description("Optional credential profile name.")] string? profile = null)
        => Json.Serialize(await insight.GetDemographicsAsync(Resolve(resolver, profile), CancellationToken.None));

    [McpServerTool(Name = "line_insight_deliveries"), Description("Get the number of messages sent on a date (date is yyyyMMdd).")]
    public static async Task<string> InsightDeliveries(
        InsightService insight, CredentialResolver resolver,
        [Description("Date in yyyyMMdd format.")] string date,
        [Description("Optional credential profile name.")] string? profile = null)
        => Json.Serialize(await insight.GetDeliveriesAsync(Resolve(resolver, profile), date, CancellationToken.None));

    [McpServerTool(Name = "line_insight_followers"), Description("Get the number of followers as of a date (date is yyyyMMdd).")]
    public static async Task<string> InsightFollowers(
        InsightService insight, CredentialResolver resolver,
        [Description("Date in yyyyMMdd format.")] string date,
        [Description("Optional credential profile name.")] string? profile = null)
        => Json.Serialize(await insight.GetFollowersAsync(Resolve(resolver, profile), date, CancellationToken.None));

    [McpServerTool(Name = "line_insight_events"), Description("Get the open/click statistics of a narrowcast/broadcast message by its request id.")]
    public static async Task<string> InsightEvents(
        InsightService insight, CredentialResolver resolver,
        [Description("Request id returned when the message was sent.")] string requestId,
        [Description("Optional credential profile name.")] string? profile = null)
        => Json.Serialize(await insight.GetEventsAsync(Resolve(resolver, profile), requestId, CancellationToken.None));

    [McpServerTool(Name = "line_insight_per_unit"), Description("Get aggregated statistics for a custom aggregation unit over a period (dates are yyyyMMdd).")]
    public static async Task<string> InsightPerUnit(
        InsightService insight, CredentialResolver resolver,
        [Description("Custom aggregation unit name.")] string unit,
        [Description("Start date in yyyyMMdd format.")] string from,
        [Description("End date in yyyyMMdd format.")] string to,
        [Description("Optional credential profile name.")] string? profile = null)
        => Json.Serialize(await insight.GetPerUnitAsync(Resolve(resolver, profile), unit, from, to, CancellationToken.None));

    [McpServerTool(Name = "line_insight_richmenu_summary"), Description("Get the aggregate display/click statistics of a rich menu over a period (dates are yyyyMMdd).")]
    public static async Task<string> InsightRichMenuSummary(
        InsightService insight, CredentialResolver resolver,
        [Description("Rich menu id.")] string richMenuId,
        [Description("Start date in yyyyMMdd format.")] string from,
        [Description("End date in yyyyMMdd format.")] string to,
        [Description("Optional credential profile name.")] string? profile = null)
        => Json.Serialize(await insight.GetRichMenuSummaryAsync(Resolve(resolver, profile), richMenuId, from, to, CancellationToken.None));

    [McpServerTool(Name = "line_insight_richmenu_daily"), Description("Get the daily display/click statistics of a rich menu over a period (dates are yyyyMMdd).")]
    public static async Task<string> InsightRichMenuDaily(
        InsightService insight, CredentialResolver resolver,
        [Description("Rich menu id.")] string richMenuId,
        [Description("Start date in yyyyMMdd format.")] string from,
        [Description("End date in yyyyMMdd format.")] string to,
        [Description("Optional credential profile name.")] string? profile = null)
        => Json.Serialize(await insight.GetRichMenuDailyAsync(Resolve(resolver, profile), richMenuId, from, to, CancellationToken.None));

    [McpServerTool(Name = "line_audience_list"), Description("List audience groups (paginated). page is 1 or higher; size defaults to 20 (max 40).")]
    public static async Task<string> AudienceList(
        AudienceService audience, CredentialResolver resolver,
        [Description("Page to return (1 or higher).")] long page = 1,
        [Description("Audiences per page (default 20, max 40).")] long size = 20,
        [Description("Optional credential profile name.")] string? profile = null)
        => Json.Serialize(await audience.ListAsync(Resolve(resolver, profile), page, size, CancellationToken.None));

    [McpServerTool(Name = "line_audience_get"), Description("Get an audience group and its jobs by id.")]
    public static async Task<string> AudienceGet(
        AudienceService audience, CredentialResolver resolver,
        [Description("Audience group id.")] long audienceGroupId,
        [Description("Optional credential profile name.")] string? profile = null)
        => Json.Serialize(await audience.GetAsync(Resolve(resolver, profile), audienceGroupId, CancellationToken.None));

    [McpServerTool(Name = "line_token_verify"), Description("Verify a channel access token's validity and remaining lifetime. Does not return the token.")]
    public static async Task<string> TokenVerify(
        TokenService tokens,
        [Description("The channel access token to verify.")] string token)
    {
        var result = await tokens.VerifyAsync(token, CancellationToken.None);
        return Json.Serialize(result);
    }

    [McpServerTool(Name = "line_webhook_verify"), Description("Verify a webhook payload's x-line-signature and summarize its events. Body and signature are supplied inline.")]
    public static async Task<string> WebhookVerify(
        WebhookService webhook, CredentialResolver resolver,
        [Description("Raw webhook request body (JSON text).")] string body,
        [Description("The x-line-signature header value.")] string signature,
        [Description("Optional credential profile name (for the channel secret).")] string? profile = null)
    {
        var secret = Resolve(resolver, profile).RequireChannelSecret();
        var result = await webhook.VerifyAsync(secret, System.Text.Encoding.UTF8.GetBytes(body), signature, CancellationToken.None);
        return Json.Serialize(result);
    }

    [McpServerTool(Name = "line_webhook_get_endpoint"), Description("Get the channel's configured webhook endpoint URL and whether it is active.")]
    public static async Task<string> WebhookGetEndpoint(
        MessageService messages, CredentialResolver resolver,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        var info = await messages.GetWebhookEndpointAsync(Resolve(resolver, profile), CancellationToken.None);
        return Json.Serialize(info);
    }

    [McpServerTool(Name = "line_webhook_test_endpoint"), Description(
        "Ask the LINE platform to send a test event to the webhook endpoint and report reachability "
        + "(statusCode, success). Diagnostic only. Pass a url (absolute https) to test that URL, or omit "
        + "it to test the currently configured endpoint.")]
    public static async Task<string> WebhookTestEndpoint(
        MessageService messages, CredentialResolver resolver,
        [Description("Endpoint URL to test (absolute https). Omit to test the configured endpoint.")] string? url = null,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        var result = await messages.TestWebhookEndpointAsync(Resolve(resolver, profile), url, CancellationToken.None);
        return Json.Serialize(result);
    }

    internal static ResolvedCredentials Resolve(CredentialResolver resolver, string? profile) =>
        resolver.Resolve(new CredentialOverrides { ProfileName = profile });
}
