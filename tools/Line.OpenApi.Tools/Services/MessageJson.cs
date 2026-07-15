using System.Text;
using System.Text.Json;
using Line.OpenApi.Messaging.Generated.Api.Models;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Serialization.Json;

namespace Line.OpenApi.Tools.Services;

/// <summary>
/// Parses user-supplied message JSON into the generated polymorphic <see cref="Message"/>
/// model. Uses a Kiota JSON parse node factory directly (not the global serialization
/// registry) so it works regardless of which clients have been constructed.
/// </summary>
internal static class MessageJson
{
    /// <summary>Parses a JSON array of message objects into generated <see cref="Message"/> instances.</summary>
    public static async Task<List<Message>> ParseMessagesAsync(string messagesJson, CancellationToken cancellationToken)
    {
        List<Message> messages;
        try
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(messagesJson));
            var node = await new JsonParseNodeFactory()
                .GetRootParseNodeAsync("application/json", stream, cancellationToken)
                .ConfigureAwait(false);

            var parsed = node?.GetCollectionOfObjectValues(Message.CreateFromDiscriminatorValue);
            messages = parsed?.Where(m => m is not null).Select(m => m!).ToList() ?? new List<Message>();
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            // Surface malformed input as the tool's own input error (exit code 2) instead of
            // leaking a raw serializer exception to the caller / MCP client.
            throw new MessageInputException($"Message JSON could not be parsed: {ex.Message}", ex);
        }

        // Kiota returns an empty (non-null) collection — not an exception — for a non-array root
        // (a single object, [], null, or a scalar). So this guard, not the parse step, is what
        // rejects "valid JSON but not a message array". LINE requires 1..5 messages; catching it
        // here makes dryRun meaningful and avoids a 400 on send.
        if (messages.Count == 0)
        {
            throw new MessageInputException(
                "Expected a non-empty JSON array of message objects, e.g. [{\"type\":\"text\",\"text\":\"hi\"}].");
        }
        return messages;
    }

    /// <summary>Builds a single-element messages array containing one text message.</summary>
    public static string TextMessagesJson(string text)
    {
        var escaped = System.Text.Json.JsonSerializer.Serialize(text);
        return $"[{{\"type\":\"text\",\"text\":{escaped}}}]";
    }

    /// <summary>Wraps raw Flex "contents" JSON into a single-element messages array with a Flex envelope.</summary>
    public static string WrapFlex(string flexContentsJson, string altText)
    {
        // The envelope is composed textually so the contents pass through verbatim.
        var escapedAlt = System.Text.Json.JsonSerializer.Serialize(altText);
        return $"[{{\"type\":\"flex\",\"altText\":{escapedAlt},\"contents\":{flexContentsJson}}}]";
    }
}

/// <summary>Thrown when message input JSON is missing or malformed. Maps to exit code 2.</summary>
public sealed class MessageInputException : Exception
{
    public MessageInputException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
