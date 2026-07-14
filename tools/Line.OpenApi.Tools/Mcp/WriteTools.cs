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
    [McpServerTool(Name = "line_message_push"), Description("SENDS a push message. Provide messages as a JSON array of LINE message objects. Side effect: delivers a message.")]
    public static async Task<string> MessagePush(
        MessageService messages, CredentialResolver resolver,
        [Description("Destination id (userId/groupId/roomId).")] string to,
        [Description("JSON array of message objects, e.g. [{\"type\":\"text\",\"text\":\"hi\"}].")] string messagesJson,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        var result = await messages.PushRawAsync(ReadTools.Resolve(resolver, profile), to, messagesJson, CancellationToken.None);
        return Json.Serialize(result);
    }

    [McpServerTool(Name = "line_message_multicast"), Description("SENDS a message to multiple users. Side effect: delivers messages.")]
    public static async Task<string> MessageMulticast(
        MessageService messages, CredentialResolver resolver,
        [Description("Destination user ids.")] string[] to,
        [Description("JSON array of message objects.")] string messagesJson,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        var result = await messages.MulticastAsync(ReadTools.Resolve(resolver, profile), to, messagesJson, CancellationToken.None);
        return Json.Serialize(result);
    }

    [McpServerTool(Name = "line_message_broadcast"), Description("SENDS a message to ALL friends of the bot. Side effect: broadcasts to everyone.")]
    public static async Task<string> MessageBroadcast(
        MessageService messages, CredentialResolver resolver,
        [Description("JSON array of message objects.")] string messagesJson,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        var result = await messages.BroadcastAsync(ReadTools.Resolve(resolver, profile), messagesJson, CancellationToken.None);
        return Json.Serialize(result);
    }

    [McpServerTool(Name = "line_message_reply"), Description("SENDS a reply message using a reply token. Side effect: delivers a message.")]
    public static async Task<string> MessageReply(
        MessageService messages, CredentialResolver resolver,
        [Description("Reply token from a webhook event.")] string replyToken,
        [Description("JSON array of message objects.")] string messagesJson,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        var result = await messages.ReplyAsync(ReadTools.Resolve(resolver, profile), replyToken, messagesJson, CancellationToken.None);
        return Json.Serialize(result);
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

    [McpServerTool(Name = "line_liff_delete"), Description("DELETES a LIFF app. Side effect: permanently removes a LIFF app.")]
    public static async Task<string> LiffDelete(
        LiffService liff, CredentialResolver resolver,
        [Description("LIFF app id.")] string liffId,
        [Description("Optional credential profile name.")] string? profile = null)
    {
        await liff.DeleteAsync(ReadTools.Resolve(resolver, profile), liffId, CancellationToken.None);
        return Json.Serialize(new { liffId, deleted = true });
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
