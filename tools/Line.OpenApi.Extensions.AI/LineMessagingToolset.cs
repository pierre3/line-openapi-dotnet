using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Line.OpenApi.Messaging;
using Line.OpenApi.Messaging.Generated.Api.Models;
using Line.OpenApi.Tools.Services; // shared source: MessageJson + flat DTOs (internal)

namespace Line.OpenApi.Extensions.AI;

/// <summary>
/// The instance that actually runs each tool. It holds the injected <see cref="MessagingClient"/>
/// and the developer's <see cref="LineAiToolOptions"/>, so every safety gate is closure-bound here
/// rather than exposed as a tool parameter (design section 5, ADR-4). Its methods carry the
/// <see cref="DescriptionAttribute"/>s that <c>AIFunctionFactory</c> reads to build the tool schema.
/// The <see cref="CancellationToken"/> parameters are bound by M.E.AI and never appear in the
/// generated JSON schema.
/// </summary>
internal sealed class LineMessagingToolset
{
    private readonly MessagingClient _client;
    private readonly LineAiToolOptions _options;

    internal LineMessagingToolset(MessagingClient client, LineAiToolOptions options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    // --- Read-only tools -------------------------------------------------------

    [Description("Gets this LINE bot's own information: userId, basic id, display name, chat mode. Returns no secrets.")]
    public async Task<BotInfo> GetBotInfoAsync(CancellationToken cancellationToken)
    {
        var res = await _client.Api.V2.Bot.Info.GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return new BotInfo(res?.UserId, res?.BasicId, res?.PremiumId, res?.DisplayName, res?.PictureUrl,
            res?.ChatMode?.ToString(), res?.MarkAsReadMode?.ToString());
    }

    [Description("Gets the monthly message quota limit for this LINE bot.")]
    public async Task<QuotaInfo> GetQuotaAsync(CancellationToken cancellationToken)
    {
        var res = await _client.Api.V2.Bot.Message.Quota.GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return new QuotaInfo(res?.Type?.ToString(), res?.Value);
    }

    [Description("Gets a LINE user's profile (display name, picture, status message, language) by user id. Returns no secrets.")]
    public async Task<ProfileInfo> GetProfileAsync(
        [Description("The target user's LINE user id (starts with 'U').")] string userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new MessageInputException("userId is required.");
        var res = await _client.Api.V2.Bot.Profile[userId].GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return new ProfileInfo(res?.UserId, res?.DisplayName, res?.PictureUrl, res?.StatusMessage, res?.Language);
    }

    [Description(
        "Validates a JSON array of LINE message objects WITHOUT sending anything (no API call). Returns "
        + "how many messages parsed and their types. Use this to check a payload before a send. "
        + "Example: [{\"type\":\"text\",\"text\":\"hi\"}].")]
    public async Task<MessageValidationResult> ValidateMessagesAsync(
        [Description("JSON array of message objects, e.g. [{\"type\":\"text\",\"text\":\"hi\"}].")] string messagesJson,
        CancellationToken cancellationToken)
    {
        var messages = await MessageJson.ParseMessagesAsync(messagesJson, cancellationToken).ConfigureAwait(false);
        return Validation(messages);
    }

    // --- Send tools ------------------------------------------------------------

    [Description(
        "SENDS a push message to one destination (user / group / room). messagesJson is a JSON array of "
        + "LINE message objects. Example: [{\"type\":\"text\",\"text\":\"hi\"}].")]
    public async Task<object> PushAsync(
        [Description("Destination id (userId / groupId / roomId).")] string to,
        [Description("JSON array of message objects, e.g. [{\"type\":\"text\",\"text\":\"hi\"}].")] string messagesJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(to)) throw new MessageInputException("to is required.");
        var messages = await MessageJson.ParseMessagesAsync(messagesJson, cancellationToken).ConfigureAwait(false);
        var context = new LineSendContext(LineSendOperation.Push, new[] { to }, messages.Count, messagesJson);
        if (await ShortCircuitAsync(context, messages, cancellationToken).ConfigureAwait(false) is { } dry) return dry;

        var res = await _client.Api.V2.Bot.Message.Push
            .PostAsync(new PushMessageRequest { To = to, Messages = messages }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return SendResult.From(res?.SentMessages);
    }

    [Description(
        "SENDS a message to multiple users (multicast). messagesJson is a JSON array of LINE message "
        + "objects. Example: [{\"type\":\"text\",\"text\":\"hi\"}].")]
    public async Task<object> MulticastAsync(
        [Description("Destination user ids.")] string[] to,
        [Description("JSON array of message objects, e.g. [{\"type\":\"text\",\"text\":\"hi\"}].")] string messagesJson,
        CancellationToken cancellationToken)
    {
        if (to is null || to.Length == 0) throw new MessageInputException("to must contain at least one user id.");
        var messages = await MessageJson.ParseMessagesAsync(messagesJson, cancellationToken).ConfigureAwait(false);
        var context = new LineSendContext(LineSendOperation.Multicast, to.ToArray(), messages.Count, messagesJson);
        if (await ShortCircuitAsync(context, messages, cancellationToken).ConfigureAwait(false) is { } dry) return dry;

        // Multicast returns an empty body on success (no per-message ids).
        await _client.Api.V2.Bot.Message.Multicast
            .PostAsync(new MulticastRequest { To = to.ToList(), Messages = messages }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return SendResult.Accepted;
    }

    [Description(
        "SENDS a reply message using a reply token from a webhook event. messagesJson is a JSON array of "
        + "LINE message objects. Example: [{\"type\":\"text\",\"text\":\"hi\"}].")]
    public async Task<object> ReplyAsync(
        [Description("Reply token from a webhook event.")] string replyToken,
        [Description("JSON array of message objects, e.g. [{\"type\":\"text\",\"text\":\"hi\"}].")] string messagesJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(replyToken)) throw new MessageInputException("replyToken is required.");
        var messages = await MessageJson.ParseMessagesAsync(messagesJson, cancellationToken).ConfigureAwait(false);
        // The reply target is implicit in the token, so Recipients is empty; Operation identifies it.
        var context = new LineSendContext(LineSendOperation.Reply, Array.Empty<string>(), messages.Count, messagesJson);
        if (await ShortCircuitAsync(context, messages, cancellationToken).ConfigureAwait(false) is { } dry) return dry;

        var res = await _client.Api.V2.Bot.Message.Reply
            .PostAsync(new ReplyMessageRequest { ReplyToken = replyToken, Messages = messages }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return SendResult.From(res?.SentMessages);
    }

    [Description(
        "SENDS a message to ALL friends of this bot (broadcast — the largest blast radius). messagesJson "
        + "is a JSON array of LINE message objects. Example: [{\"type\":\"text\",\"text\":\"hi\"}].")]
    public async Task<object> BroadcastAsync(
        [Description("JSON array of message objects, e.g. [{\"type\":\"text\",\"text\":\"hi\"}].")] string messagesJson,
        CancellationToken cancellationToken)
    {
        var messages = await MessageJson.ParseMessagesAsync(messagesJson, cancellationToken).ConfigureAwait(false);
        // No explicit destination: Recipients is empty and Operation is Broadcast.
        var context = new LineSendContext(LineSendOperation.Broadcast, Array.Empty<string>(), messages.Count, messagesJson);
        if (await ShortCircuitAsync(context, messages, cancellationToken).ConfigureAwait(false) is { } dry) return dry;

        // Broadcast returns an empty body on success.
        await _client.Api.V2.Bot.Message.Broadcast
            .PostAsync(new BroadcastRequest { Messages = messages }, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return SendResult.Accepted;
    }

    // --- Gate pipeline ---------------------------------------------------------

    /// <summary>
    /// Evaluates the developer-set gates before a send. Returns a non-null validation result to
    /// short-circuit (DryRun), or null to proceed with the actual send. Throws
    /// <see cref="LineSendRefusedException"/> when a gate refuses. Guarantees no transport is touched
    /// on DryRun or refusal (design section 5.2 invariant).
    /// </summary>
    private async Task<object?> ShortCircuitAsync(LineSendContext context, List<Message> messages, CancellationToken cancellationToken)
    {
        if (_options.DryRun)
        {
            return Validation(messages);
        }

        if (_options.SendPolicy is { } policy &&
            !await policy(context, cancellationToken).ConfigureAwait(false))
        {
            throw new LineSendRefusedException(context, LineSendRefusalStage.Policy);
        }

        if (_options.BeforeSend is { } beforeSend &&
            !await beforeSend(context, cancellationToken).ConfigureAwait(false))
        {
            throw new LineSendRefusedException(context, LineSendRefusalStage.BeforeSend);
        }

        return null;
    }

    private static MessageValidationResult Validation(List<Message> messages) =>
        new(DryRun: true, Valid: true, Count: messages.Count, MessageTypes: messages.Select(m => m.GetType().Name).ToList());
}
