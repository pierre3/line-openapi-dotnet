using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Line.OpenApi.Extensions.AI;

/// <summary>The kind of send operation a tool is about to perform.</summary>
public enum LineSendOperation
{
    /// <summary>Push to a single destination (user / group / room).</summary>
    Push,

    /// <summary>Multicast to an explicit set of user ids.</summary>
    Multicast,

    /// <summary>Reply using a reply token from a webhook event.</summary>
    Reply,

    /// <summary>Broadcast to every friend of the bot (no explicit destination).</summary>
    Broadcast,
}

/// <summary>
/// The context passed to <see cref="LineSendPolicy"/> and <see cref="LineBeforeSendHook"/> before a
/// message is sent. Carries enough to reason about blast radius (operation, recipients, message
/// count) and about content (the raw messages JSON, for human-in-the-loop review).
/// </summary>
/// <param name="Operation">The send operation kind.</param>
/// <param name="Recipients">
/// The explicit destination ids: one for <see cref="LineSendOperation.Push"/>, the full set for
/// <see cref="LineSendOperation.Multicast"/>, and empty for <see cref="LineSendOperation.Reply"/>
/// (the target is implicit in the reply token) and <see cref="LineSendOperation.Broadcast"/>
/// (all friends). A policy detects a broadcast by <see cref="Operation"/>, not by an empty set.
/// </param>
/// <param name="MessageCount">The number of message objects in the payload (LINE allows 1..5).</param>
/// <param name="MessagesJson">The raw messages JSON, for content inspection / approval.</param>
public sealed record LineSendContext(
    LineSendOperation Operation,
    IReadOnlyList<string> Recipients,
    int MessageCount,
    string MessagesJson);

/// <summary>
/// A send policy evaluated before every send. Return <c>false</c> to refuse the send (the tool
/// then surfaces a <see cref="LineSendRefusedException"/> to the caller instead of contacting the
/// API). Use it to bound blast radius: allowed destinations, recipient count, message count, or a
/// remote allow-list lookup (hence async). It is set by the developer at tool-creation time and is
/// never exposed as an LLM-visible tool argument.
/// </summary>
public delegate ValueTask<bool> LineSendPolicy(LineSendContext context, CancellationToken cancellationToken);

/// <summary>
/// A human-in-the-loop / audit hook invoked immediately before a send, after
/// <see cref="LineSendPolicy"/> allows it. Return <c>false</c> to refuse. Unlike the policy (which
/// reasons about destination and volume), this hook is the place to inspect and approve the message
/// <em>content</em> — the defense against a prompt-injection-driven "legitimate recipient, malicious
/// content" send. It is set by the developer at tool-creation time and is never an LLM-visible
/// argument.
/// </summary>
public delegate ValueTask<bool> LineBeforeSendHook(LineSendContext context, CancellationToken cancellationToken);

/// <summary>
/// Thrown when a send is refused by <see cref="LineSendPolicy"/> or <see cref="LineBeforeSendHook"/>.
/// The message is never delivered; no API call is made.
/// </summary>
public sealed class LineSendRefusedException : Exception
{
    /// <summary>The context that was refused.</summary>
    public LineSendContext Context { get; }

    /// <summary>Which gate refused: the policy or the before-send hook.</summary>
    public LineSendRefusalStage Stage { get; }

    internal LineSendRefusedException(LineSendContext context, LineSendRefusalStage stage)
        : base(BuildMessage(context, stage))
    {
        Context = context;
        Stage = stage;
    }

    private static string BuildMessage(LineSendContext context, LineSendRefusalStage stage)
    {
        var gate = stage == LineSendRefusalStage.Policy ? "send policy" : "before-send hook";
        // Deliberately omits message content so a refusal message never echoes payload text.
        return $"The {context.Operation} send was refused by the {gate}.";
    }
}

/// <summary>Identifies which safety gate refused a send.</summary>
public enum LineSendRefusalStage
{
    /// <summary>Refused by <see cref="LineSendPolicy"/>.</summary>
    Policy,

    /// <summary>Refused by <see cref="LineBeforeSendHook"/>.</summary>
    BeforeSend,
}
