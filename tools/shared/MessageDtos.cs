using System.Collections.Generic;
using System.Linq;
using Line.OpenApi.Messaging.Generated.Api.Models;

namespace Line.OpenApi.Tools.Services;

// Flat DTOs returned to callers (CLI/MCP and the AI layer) instead of the generated Kiota
// models. Shared source (compiled into both Line.OpenApi.Tools and Line.OpenApi.Extensions.AI);
// the namespace is preserved so the Tools assembly sees the same types as before. They are
// internal: each consumer serializes them to JSON (CLI output / AI tool result), so they are
// an implementation detail and must not appear on a package's public API surface.

/// <summary>Outcome of a send operation. Lists returned sent-message ids when the API provides them.</summary>
internal sealed record SendResult(IReadOnlyList<string> SentMessageIds)
{
    /// <summary>Result for endpoints that return an empty body on success (multicast/broadcast).</summary>
    public static readonly SendResult Accepted = new(new List<string>());

    public static SendResult From(List<SentMessage>? sent) =>
        new(sent?.Where(s => s.Id is not null).Select(s => s.Id!).ToList() ?? new List<string>());
}

/// <summary>Outcome of a dry-run message validation: the input parsed to <c>Count</c> messages of the listed types.</summary>
internal sealed record MessageValidationResult(bool DryRun, bool Valid, int Count, IReadOnlyList<string> MessageTypes);

/// <summary>Bot information (non-secret).</summary>
internal sealed record BotInfo(
    string? UserId, string? BasicId, string? PremiumId, string? DisplayName, string? PictureUrl,
    string? ChatMode, string? MarkAsReadMode);

/// <summary>Message quota.</summary>
internal sealed record QuotaInfo(string? Type, long? Value);

/// <summary>User profile (non-secret).</summary>
internal sealed record ProfileInfo(string? UserId, string? DisplayName, string? PictureUrl, string? StatusMessage, string? Language);
