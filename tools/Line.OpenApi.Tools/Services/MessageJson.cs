using System.Text;
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
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(messagesJson));
        var node = await new JsonParseNodeFactory()
            .GetRootParseNodeAsync("application/json", stream, cancellationToken)
            .ConfigureAwait(false);

        var messages = node.GetCollectionOfObjectValues(Message.CreateFromDiscriminatorValue);
        return messages?.Where(m => m is not null).Select(m => m!).ToList()
            ?? throw new MessageInputException("No messages could be parsed from the input JSON.");
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
