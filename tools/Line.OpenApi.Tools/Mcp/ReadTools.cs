using System.ComponentModel;
using Line.OpenApi.Tools.Configuration;
using Line.OpenApi.Tools.Output;
using Line.OpenApi.Tools.Services;
using ModelContextProtocol.Server;

namespace Line.OpenApi.Tools.Mcp;

/// <summary>
/// Read-only MCP tools (safe under <c>--read-only</c>): bot lookup, LIFF listing, token verify,
/// webhook verify. Results are returned as JSON text and contain no secrets. Tool names follow
/// <c>line_&lt;area&gt;_&lt;verb&gt;</c> (spec §4.5).
/// </summary>
[McpServerToolType]
internal class ReadTools
{
    [McpServerTool(Name = "line_ping"), Description("Health check that returns \"pong\".")]
    public static string Ping() => "pong";

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

    internal static ResolvedCredentials Resolve(CredentialResolver resolver, string? profile) =>
        resolver.Resolve(new CredentialOverrides { ProfileName = profile });
}
