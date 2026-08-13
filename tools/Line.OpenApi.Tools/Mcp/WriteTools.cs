using System.ComponentModel;
using Line.OpenApi.Tools.Configuration;
using Line.OpenApi.Tools.Output;
using Line.OpenApi.Tools.Services;
using ModelContextProtocol.Server;

namespace Line.OpenApi.Tools.Mcp;

/// <summary>
/// Mutating MCP tools (excluded under <c>--read-only</c>): message send, LIFF add/update/delete,
/// token issue/revoke, webhook replay. Each description notes the side effect. Tool names follow
/// <c>line_&lt;area&gt;_&lt;verb&gt;</c> (spec §4.5).
/// </summary>
[McpServerToolType]
internal class WriteTools
{
    // Minimal shapes for the simple message types, shared across the send-tool descriptions so the
    // model can build them without calling line_message_schema. Flex/template are large and
    // self-recursive — fetch those from line_message_schema instead.
    private const string SimpleMessageExamples =
        "Simple examples: {\"type\":\"text\",\"text\":\"hi\"}; "
        + "{\"type\":\"sticker\",\"packageId\":\"446\",\"stickerId\":\"1988\"}; "
        + "{\"type\":\"image\",\"originalContentUrl\":\"https://.../a.jpg\",\"previewImageUrl\":\"https://.../p.jpg\"}. "
        + "For flex or template, call line_message_schema first. Use dryRun=true to type-check without sending.";

    [McpServerTool(Name = "line_message_push"), Description(
        "SENDS a push message. Provide messages as a JSON array of LINE message objects. "
        + "Side effect: delivers a message (unless dryRun=true). " + SimpleMessageExamples)]
    public static async Task<string> MessagePush(
        MessageService messages, CredentialResolver resolver,
        [Description("Destination id (userId/groupId/roomId).")] string to,
        [Description("JSON array of message objects, e.g. [{\"type\":\"text\",\"text\":\"hi\"}].")] string messagesJson,
        [Description("If true, validate the messages and return their parsed types WITHOUT sending (no API call).")] bool dryRun = false,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        if (dryRun)
        {
            return Json.Serialize(await messages.ValidateMessagesAsync(messagesJson, CancellationToken.None));
        }
        var result = await messages.PushRawAsync(ReadTools.Resolve(resolver, profile), to, messagesJson, CancellationToken.None);
        return Json.Serialize(result);
    }

    [McpServerTool(Name = "line_message_multicast"), Description(
        "SENDS a message to multiple users. Side effect: delivers messages (unless dryRun=true). " + SimpleMessageExamples)]
    public static async Task<string> MessageMulticast(
        MessageService messages, CredentialResolver resolver,
        [Description("Destination user ids.")] string[] to,
        [Description("JSON array of message objects, e.g. [{\"type\":\"text\",\"text\":\"hi\"}].")] string messagesJson,
        [Description("If true, validate the messages and return their parsed types WITHOUT sending (no API call).")] bool dryRun = false,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        if (dryRun)
        {
            return Json.Serialize(await messages.ValidateMessagesAsync(messagesJson, CancellationToken.None));
        }
        var result = await messages.MulticastAsync(ReadTools.Resolve(resolver, profile), to, messagesJson, CancellationToken.None);
        return Json.Serialize(result);
    }

    [McpServerTool(Name = "line_message_broadcast"), Description(
        "SENDS a message to ALL friends of the bot. Side effect: broadcasts to everyone (unless dryRun=true). " + SimpleMessageExamples)]
    public static async Task<string> MessageBroadcast(
        MessageService messages, CredentialResolver resolver,
        [Description("JSON array of message objects, e.g. [{\"type\":\"text\",\"text\":\"hi\"}].")] string messagesJson,
        [Description("If true, validate the messages and return their parsed types WITHOUT sending (no API call).")] bool dryRun = false,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        if (dryRun)
        {
            return Json.Serialize(await messages.ValidateMessagesAsync(messagesJson, CancellationToken.None));
        }
        var result = await messages.BroadcastAsync(ReadTools.Resolve(resolver, profile), messagesJson, CancellationToken.None);
        return Json.Serialize(result);
    }

    [McpServerTool(Name = "line_message_reply"), Description(
        "SENDS a reply message using a reply token. Side effect: delivers a message (unless dryRun=true). " + SimpleMessageExamples)]
    public static async Task<string> MessageReply(
        MessageService messages, CredentialResolver resolver,
        [Description("Reply token from a webhook event.")] string replyToken,
        [Description("JSON array of message objects, e.g. [{\"type\":\"text\",\"text\":\"hi\"}].")] string messagesJson,
        [Description("If true, validate the messages and return their parsed types WITHOUT sending (no API call).")] bool dryRun = false,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        if (dryRun)
        {
            return Json.Serialize(await messages.ValidateMessagesAsync(messagesJson, CancellationToken.None));
        }
        var result = await messages.ReplyAsync(ReadTools.Resolve(resolver, profile), replyToken, messagesJson, CancellationToken.None);
        return Json.Serialize(result);
    }

    [McpServerTool(Name = "line_richmenu_create"), Description(
        "CREATES a rich menu from a JSON definition (see line_richmenu_schema). Side effect: creates a "
        + "rich menu and returns its id. Set dryRun=true to validate the definition via the LINE "
        + "validation endpoint WITHOUT creating it. After creating, upload the image with the CLI "
        + "(`line richmenu image <id> --file menu.png`) then set it as default (line_richmenu_set_default) "
        + "or link it to a user (line_richmenu_link).")]
    public static async Task<string> RichMenuCreate(
        RichMenuService richMenu, CredentialResolver resolver,
        [Description("Rich menu definition JSON (matches line_richmenu_schema).")] string richMenuJson,
        [Description("If true, validate the definition via the LINE validation endpoint WITHOUT creating it. Note: unlike message dryRun, this still requires credentials and makes an API call.")] bool dryRun = false,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        var credentials = ReadTools.Resolve(resolver, profile);
        if (dryRun)
        {
            return Json.Serialize(await richMenu.ValidateAsync(credentials, richMenuJson, CancellationToken.None));
        }
        var richMenuId = await richMenu.CreateAsync(credentials, richMenuJson, CancellationToken.None);
        return Json.Serialize(new { richMenuId });
    }

    [McpServerTool(Name = "line_richmenu_delete"), Description("DELETES a rich menu. Side effect: permanently removes a rich menu.")]
    public static async Task<string> RichMenuDelete(
        RichMenuService richMenu, CredentialResolver resolver,
        [Description("Rich menu id.")] string richMenuId,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        await richMenu.DeleteAsync(ReadTools.Resolve(resolver, profile), richMenuId, CancellationToken.None);
        return Json.Serialize(new { richMenuId, deleted = true });
    }

    [McpServerTool(Name = "line_richmenu_set_default"), Description("SETS the default rich menu for ALL users. Side effect: changes what every user sees.")]
    public static async Task<string> RichMenuSetDefault(
        RichMenuService richMenu, CredentialResolver resolver,
        [Description("Rich menu id.")] string richMenuId,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        await richMenu.SetDefaultAsync(ReadTools.Resolve(resolver, profile), richMenuId, CancellationToken.None);
        return Json.Serialize(new { richMenuId, isDefault = true });
    }

    [McpServerTool(Name = "line_richmenu_cancel_default"), Description("CANCELS the default rich menu. Side effect: removes the default for all users.")]
    public static async Task<string> RichMenuCancelDefault(
        RichMenuService richMenu, CredentialResolver resolver,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        await richMenu.CancelDefaultAsync(ReadTools.Resolve(resolver, profile), CancellationToken.None);
        return Json.Serialize(new { cancelled = true });
    }

    [McpServerTool(Name = "line_richmenu_link"), Description("LINKS a rich menu to a specific user. Side effect: changes what that user sees.")]
    public static async Task<string> RichMenuLink(
        RichMenuService richMenu, CredentialResolver resolver,
        [Description("Target user id.")] string userId,
        [Description("Rich menu id.")] string richMenuId,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        await richMenu.LinkToUserAsync(ReadTools.Resolve(resolver, profile), userId, richMenuId, CancellationToken.None);
        return Json.Serialize(new { userId, richMenuId, linked = true });
    }

    [McpServerTool(Name = "line_richmenu_unlink"), Description("UNLINKS the rich menu from a specific user. Side effect: removes that user's rich menu.")]
    public static async Task<string> RichMenuUnlink(
        RichMenuService richMenu, CredentialResolver resolver,
        [Description("Target user id.")] string userId,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        await richMenu.UnlinkFromUserAsync(ReadTools.Resolve(resolver, profile), userId, CancellationToken.None);
        return Json.Serialize(new { userId, unlinked = true });
    }

    [McpServerTool(Name = "line_liff_add"), Description("ADDS a LIFF app from a JSON definition. Side effect: creates a LIFF app.")]
    public static async Task<string> LiffAdd(
        LiffService liff, CredentialResolver resolver,
        [Description("LIFF app definition JSON.")] string appJson,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        var liffId = await liff.AddAsync(ReadTools.Resolve(resolver, profile), appJson, CancellationToken.None);
        return Json.Serialize(new { liffId });
    }

    [McpServerTool(Name = "line_liff_update"), Description("UPDATES a LIFF app from a JSON definition. Side effect: modifies a LIFF app.")]
    public static async Task<string> LiffUpdate(
        LiffService liff, CredentialResolver resolver,
        [Description("LIFF app id.")] string liffId,
        [Description("LIFF app definition JSON.")] string appJson,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        await liff.UpdateAsync(ReadTools.Resolve(resolver, profile), liffId, appJson, CancellationToken.None);
        return Json.Serialize(new { liffId, updated = true });
    }

    [McpServerTool(Name = "line_liff_update_url"), Description(
        "UPDATES only a LIFF app's endpoint URL (view.url) via a partial update — handy for repointing "
        + "at a fresh dev-tunnel URL. The url must be absolute https. Side effect: modifies a LIFF app. "
        + "Use line_liff_list to find the liffId.")]
    public static async Task<string> LiffUpdateUrl(
        LiffService liff, CredentialResolver resolver,
        [Description("LIFF app id.")] string liffId,
        [Description("New endpoint URL (absolute https).")] string url,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        await liff.UpdateUrlAsync(ReadTools.Resolve(resolver, profile), liffId, url, CancellationToken.None);
        return Json.Serialize(new { liffId, url, updated = true });
    }

    [McpServerTool(Name = "line_liff_delete"), Description("DELETES a LIFF app. Side effect: permanently removes a LIFF app.")]
    public static async Task<string> LiffDelete(
        LiffService liff, CredentialResolver resolver,
        [Description("LIFF app id.")] string liffId,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        await liff.DeleteAsync(ReadTools.Resolve(resolver, profile), liffId, CancellationToken.None);
        return Json.Serialize(new { liffId, deleted = true });
    }

    [McpServerTool(Name = "line_audience_create"), Description(
        "CREATES an audience group and adds the initial user IDs from a JSON request body "
        + "(CreateAudienceGroupRequest: description, isIfaAudience, audiences[]). Side effect: creates an "
        + "audience group and returns its id. To upload IDs from a file instead, use the CLI "
        + "(`line audience upload-file --file ids.txt`).")]
    public static async Task<string> AudienceCreate(
        AudienceService audience, CredentialResolver resolver,
        [Description("CreateAudienceGroupRequest JSON.")] string requestJson,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        var audienceGroupId = await audience.CreateAsync(ReadTools.Resolve(resolver, profile), requestJson, CancellationToken.None);
        return Json.Serialize(new { audienceGroupId });
    }

    [McpServerTool(Name = "line_audience_add_users"), Description(
        "ADDS user IDs to an existing upload audience group from a JSON request body "
        + "(AddAudienceToAudienceGroupRequest: audienceGroupId, audiences[]). Side effect: modifies an "
        + "audience group. To add IDs from a file instead, use the CLI (`line audience add-file <id> --file ids.txt`).")]
    public static async Task<string> AudienceAddUsers(
        AudienceService audience, CredentialResolver resolver,
        [Description("AddAudienceToAudienceGroupRequest JSON (carries audienceGroupId).")] string requestJson,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        await audience.AddUsersAsync(ReadTools.Resolve(resolver, profile), requestJson, CancellationToken.None);
        return Json.Serialize(new { added = true });
    }

    [McpServerTool(Name = "line_audience_delete"), Description("DELETES an audience group. Side effect: permanently removes an audience group.")]
    public static async Task<string> AudienceDelete(
        AudienceService audience, CredentialResolver resolver,
        [Description("Audience group id.")] long audienceGroupId,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        await audience.DeleteAsync(ReadTools.Resolve(resolver, profile), audienceGroupId, CancellationToken.None);
        return Json.Serialize(new { audienceGroupId, deleted = true });
    }

    [McpServerTool(Name = "line_shop_mission"), Description(
        "SENDS a mission sticker to a user from a JSON request body (MissionStickerRequest: to, productId, "
        + "productType='STICKER', sendPresentMessage). Side effect: sends a present to the user.")]
    public static async Task<string> ShopMission(
        ShopService shop, CredentialResolver resolver,
        [Description("MissionStickerRequest JSON.")] string requestJson,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        await shop.SendMissionAsync(ReadTools.Resolve(resolver, profile), requestJson, CancellationToken.None);
        return Json.Serialize(new { sent = true });
    }

    [McpServerTool(Name = "line_token_issue"), Description(
        "Issues a channel access token and STORES it into the local profile. By default the raw token is NOT returned "
        + "(only metadata + a masked value); subsequent tools use the stored profile. Set reveal=true to return the raw "
        + "token, which requires the server to be started with --allow-secret-output.")]
    public static async Task<string> TokenIssue(
        TokenService tokens, CredentialResolver resolver, ConfigStore config, McpToolOptions options,
        [Description("Token kind: v2.1 or stateless.")] string kind = "v2.1",
        [Description("Requested lifetime in days (v2.1, max 30).")] int days = 30,
        [Description("Return the raw token (requires server --allow-secret-output).")] bool reveal = false,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        var credentials = ReadTools.Resolve(resolver, profile);
        var tokenKind = string.Equals(kind, "stateless", StringComparison.OrdinalIgnoreCase) ? TokenKind.Stateless : TokenKind.V21;
        var result = await tokens.IssueAsync(credentials, tokenKind, TimeSpan.FromDays(days), CancellationToken.None);

        // C (default): store to profile so mutating tools gain the capability without the secret
        // ever entering model context.
        config.StoreAccessToken(credentials.ProfileName, result.AccessToken);

        return Json.Serialize(BuildIssueResponse(result, credentials.ProfileName, reveal, options.AllowSecretOutput));
    }

    /// <summary>
    /// Builds the <c>line_token_issue</c> response. The raw token is included only when the caller
    /// requested it AND the server permits secret output (spec §4.5). Extracted for unit testing
    /// of this security-critical invariant.
    /// </summary>
    internal static TokenIssueMcpResult BuildIssueResponse(TokenIssueResult result, string profileName, bool reveal, bool allowSecretOutput)
    {
        var revealAllowed = reveal && allowSecretOutput;
        return new TokenIssueMcpResult(
            TokenType: result.Kind.ToString(),
            ExpiresInSeconds: result.Lifetime is { } l ? (long?)l.TotalSeconds : null,
            KeyId: result.KeyId,
            MaskedToken: SecretMasking.Mask(result.AccessToken),
            StoredProfile: profileName,
            AccessToken: revealAllowed ? result.AccessToken : null,
            RevealDenied: reveal && !allowSecretOutput ? "server started without --allow-secret-output" : null);
    }

    [McpServerTool(Name = "line_token_revoke"), Description("REVOKES a channel access token (requires channel id and secret in the profile). Side effect: invalidates the token.")]
    public static async Task<string> TokenRevoke(
        TokenService tokens, CredentialResolver resolver,
        [Description("The channel access token to revoke.")] string token,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        await tokens.RevokeAsync(ReadTools.Resolve(resolver, profile), token, CancellationToken.None);
        return Json.Serialize(new { revoked = true });
    }

    [McpServerTool(Name = "line_webhook_replay"), Description("POSTs a webhook payload to a LOCAL (loopback) URL for debugging. No signature is added. By default only loopback destinations are allowed; start the server with --allow-remote-replay to permit remote URLs. Side effect: sends an HTTP request to the given URL.")]
    public static async Task<string> WebhookReplay(
        WebhookService webhook, McpToolOptions options,
        [Description("Raw webhook request body (JSON text).")] string body,
        [Description("Destination URL (loopback by default, e.g. http://localhost:5000/webhook).")] string to)
    {
        if (!Uri.TryCreate(to, UriKind.Absolute, out var target))
        {
            throw new MessageInputException($"Invalid destination URL: '{to}'.");
        }

        // SSRF mitigation: over MCP the URL is chosen by the model, so restrict to loopback unless
        // the operator explicitly opted in (security gate Medium#2).
        if (!target.IsLoopback && !options.AllowRemoteReplay)
        {
            throw new MessageInputException(
                $"Refusing to replay to non-loopback URL '{target}' over MCP. "
                + "Restart the server with --allow-remote-replay to permit remote destinations.");
        }

        var result = await webhook.ReplayAsync(System.Text.Encoding.UTF8.GetBytes(body), target, CancellationToken.None);
        return Json.Serialize(result);
    }

    [McpServerTool(Name = "line_webhook_set_endpoint"), Description(
        "SETS the channel's webhook endpoint URL (e.g. a fresh dev-tunnel URL), so the LINE platform "
        + "delivers webhook events there. The url must be absolute https. Side effect: changes the LINE-side "
        + "webhook URL for this channel.")]
    public static async Task<string> WebhookSetEndpoint(
        MessageService messages, CredentialResolver resolver,
        [Description("The webhook endpoint URL (absolute https).")] string url,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        await messages.SetWebhookEndpointAsync(ReadTools.Resolve(resolver, profile), url, CancellationToken.None);
        return Json.Serialize(new { endpoint = url, updated = true });
    }
}

/// <summary>Response shape of <c>line_token_issue</c> (spec §4.5). <c>AccessToken</c> is null unless revealed.</summary>
public sealed record TokenIssueMcpResult(
    string TokenType,
    long? ExpiresInSeconds,
    string? KeyId,
    string MaskedToken,
    string StoredProfile,
    string? AccessToken,
    string? RevealDenied);
