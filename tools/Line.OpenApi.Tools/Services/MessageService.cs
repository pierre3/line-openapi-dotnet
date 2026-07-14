using System.Collections.Concurrent;
using Line.OpenApi.Tools.Configuration;
using Line.OpenApi.Messaging;
using Line.OpenApi.Messaging.Generated.Api.Models;

namespace Line.OpenApi.Tools.Services;

/// <summary>
/// B. Message send and bot lookup. Thin wrapper over the <see cref="MessagingClient"/> facade
/// (control host via <c>Api</c>, data host via <c>Blob</c>); host routing / BaseUrl handling
/// (R1) is already solved inside the facade, so this service does not re-implement it.
/// </summary>
public sealed class MessageService
{
    // Clients (and their HttpClients) are memoized per access token so the long-running MCP
    // server does not accumulate handlers/sockets on every call (code gate Medium#1). The facade
    // builds no BaseAddress, so a per-token instance is safe to reuse across calls.
    private static readonly ConcurrentDictionary<string, MessagingClient> Clients = new(StringComparer.Ordinal);

    private static MessagingClient Create(ResolvedCredentials credentials) =>
        Clients.GetOrAdd(credentials.RequireAccessToken(), static token => MessagingClient.CreateWithStaticToken(token));

    /// <summary>Sends a push message built from a single text string.</summary>
    public Task<SendResult> PushTextAsync(ResolvedCredentials credentials, string to, string text, CancellationToken cancellationToken) =>
        PushAsync(credentials, to, new List<Message> { new TextMessage { Text = text } }, cancellationToken);

    /// <summary>Sends a push message built from a JSON array of message objects.</summary>
    public async Task<SendResult> PushRawAsync(ResolvedCredentials credentials, string to, string messagesJson, CancellationToken cancellationToken)
    {
        var messages = await MessageJson.ParseMessagesAsync(messagesJson, cancellationToken).ConfigureAwait(false);
        return await PushAsync(credentials, to, messages, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SendResult> PushAsync(ResolvedCredentials credentials, string to, List<Message> messages, CancellationToken cancellationToken)
    {
        var client = Create(credentials);
        var res = await client.Api.V2.Bot.Message.Push
            .PostAsync(new PushMessageRequest { To = to, Messages = messages }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return SendResult.From(res?.SentMessages);
    }

    /// <summary>Sends a multicast message (JSON array of message objects) to multiple users.</summary>
    public async Task<SendResult> MulticastAsync(ResolvedCredentials credentials, IReadOnlyList<string> to, string messagesJson, CancellationToken cancellationToken)
    {
        var messages = await MessageJson.ParseMessagesAsync(messagesJson, cancellationToken).ConfigureAwait(false);
        var client = Create(credentials);
        // Multicast returns an empty body on success (no per-message ids).
        await client.Api.V2.Bot.Message.Multicast
            .PostAsync(new MulticastRequest { To = to.ToList(), Messages = messages }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return SendResult.Accepted;
    }

    /// <summary>Sends a broadcast message (JSON array of message objects) to all friends.</summary>
    public async Task<SendResult> BroadcastAsync(ResolvedCredentials credentials, string messagesJson, CancellationToken cancellationToken)
    {
        var messages = await MessageJson.ParseMessagesAsync(messagesJson, cancellationToken).ConfigureAwait(false);
        var client = Create(credentials);
        // Broadcast returns an empty body on success.
        await client.Api.V2.Bot.Message.Broadcast
            .PostAsync(new BroadcastRequest { Messages = messages }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return SendResult.Accepted;
    }

    /// <summary>Sends a reply message (JSON array of message objects) for a reply token.</summary>
    public async Task<SendResult> ReplyAsync(ResolvedCredentials credentials, string replyToken, string messagesJson, CancellationToken cancellationToken)
    {
        var messages = await MessageJson.ParseMessagesAsync(messagesJson, cancellationToken).ConfigureAwait(false);
        var client = Create(credentials);
        var res = await client.Api.V2.Bot.Message.Reply
            .PostAsync(new ReplyMessageRequest { ReplyToken = replyToken, Messages = messages }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return SendResult.From(res?.SentMessages);
    }

    /// <summary>Gets bot information.</summary>
    public async Task<BotInfo> GetBotInfoAsync(ResolvedCredentials credentials, CancellationToken cancellationToken)
    {
        var client = Create(credentials);
        var res = await client.Api.V2.Bot.Info.GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return new BotInfo(res?.UserId, res?.BasicId, res?.PremiumId, res?.DisplayName, res?.PictureUrl,
            res?.ChatMode?.ToString(), res?.MarkAsReadMode?.ToString());
    }

    /// <summary>Gets the message quota.</summary>
    public async Task<QuotaInfo> GetQuotaAsync(ResolvedCredentials credentials, CancellationToken cancellationToken)
    {
        var client = Create(credentials);
        var res = await client.Api.V2.Bot.Message.Quota.GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return new QuotaInfo(res?.Type?.ToString(), res?.Value);
    }

    /// <summary>Gets the current month's message consumption.</summary>
    public async Task<long?> GetQuotaConsumptionAsync(ResolvedCredentials credentials, CancellationToken cancellationToken)
    {
        var client = Create(credentials);
        var res = await client.Api.V2.Bot.Message.Quota.Consumption.GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return res?.TotalUsage;
    }

    /// <summary>Gets a user profile.</summary>
    public async Task<ProfileInfo> GetProfileAsync(ResolvedCredentials credentials, string userId, CancellationToken cancellationToken)
    {
        var client = Create(credentials);
        var res = await client.Api.V2.Bot.Profile[userId].GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return new ProfileInfo(res?.UserId, res?.DisplayName, res?.PictureUrl, res?.StatusMessage, res?.Language);
    }

    /// <summary>Downloads a message's binary content (data host) to a file.</summary>
    public async Task<ContentResult> DownloadContentAsync(ResolvedCredentials credentials, string messageId, string outputPath, CancellationToken cancellationToken)
    {
        var client = Create(credentials);
        await using var content = await client.Blob.V2.Bot.Message[messageId].Content
            .GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (content is null)
        {
            throw new InvalidOperationException($"No content returned for message '{messageId}'.");
        }

        await using var file = File.Create(outputPath);
        await content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        return new ContentResult(outputPath, file.Length);
    }
}

/// <summary>Outcome of a send operation. Lists returned sent-message ids when the API provides them.</summary>
public sealed record SendResult(IReadOnlyList<string> SentMessageIds)
{
    /// <summary>Result for endpoints that return an empty body on success (multicast/broadcast).</summary>
    public static readonly SendResult Accepted = new(new List<string>());

    internal static SendResult From(List<SentMessage>? sent) =>
        new(sent?.Where(s => s.Id is not null).Select(s => s.Id!).ToList() ?? new List<string>());
}

/// <summary>Bot information (non-secret).</summary>
public sealed record BotInfo(
    string? UserId, string? BasicId, string? PremiumId, string? DisplayName, string? PictureUrl,
    string? ChatMode, string? MarkAsReadMode);

/// <summary>Message quota.</summary>
public sealed record QuotaInfo(string? Type, long? Value);

/// <summary>User profile (non-secret).</summary>
public sealed record ProfileInfo(string? UserId, string? DisplayName, string? PictureUrl, string? StatusMessage, string? Language);

/// <summary>Downloaded content descriptor.</summary>
public sealed record ContentResult(string Path, long Bytes);
