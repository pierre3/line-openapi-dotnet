namespace Line.OpenApi.Extensions.AI;

/// <summary>
/// Configures which LINE tools are produced and how sends are gated. All gates are set here, by the
/// developer, at tool-creation time — never as LLM-visible tool arguments — so a model cannot flip
/// them (design section 5, ADR-4). The read-only lookup tools (bot info / quota / profile) and the
/// send-less <c>line_message_validate</c> tool are always produced regardless of these settings.
/// </summary>
public sealed class LineAiToolOptions
{
    /// <summary>
    /// Enables the send tools (<c>line_message_push</c> / <c>_multicast</c> / <c>_reply</c>). When
    /// <c>false</c> (the default), no send tool is produced and the toolset is read-only. Safe by
    /// default: a model cannot send anything unless the developer opts in.
    /// </summary>
    public bool EnableSending { get; set; }

    /// <summary>
    /// Enables the broadcast tool (<c>line_message_broadcast</c>), which sends to every friend of the
    /// bot — the largest blast radius. Requires <see cref="EnableSending"/> as well. Independent
    /// opt-in, <c>false</c> by default, so enabling ordinary sends does not implicitly enable
    /// broadcast.
    /// </summary>
    public bool AllowBroadcast { get; set; }

    /// <summary>
    /// When <c>true</c>, the send tools validate the message payload (type-check) and return the
    /// parsed result WITHOUT contacting the API — no send request is ever issued. A developer-set
    /// gate, not an LLM argument. Because nothing is sent, <see cref="SendPolicy"/> and
    /// <see cref="BeforeSend"/> are not evaluated in this mode.
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// Optional policy evaluated before every send to bound blast radius (operation / recipients /
    /// count). Returning <c>false</c> refuses the send. When <c>null</c>, no policy restriction is
    /// applied (the developer's explicit choice when <see cref="EnableSending"/> is set).
    /// </summary>
    public LineSendPolicy? SendPolicy { get; set; }

    /// <summary>
    /// Optional human-in-the-loop / audit hook invoked immediately before a send (after
    /// <see cref="SendPolicy"/>). Returning <c>false</c> refuses the send. The place to review
    /// message <em>content</em>. When <c>null</c>, no approval step is applied.
    /// </summary>
    public LineBeforeSendHook? BeforeSend { get; set; }
}
