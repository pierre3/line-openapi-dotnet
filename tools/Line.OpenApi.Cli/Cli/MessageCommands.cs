using Cocona;
using Line.OpenApi.Cli.Output;
using Line.OpenApi.Cli.Services;

namespace Line.OpenApi.Cli.Cli;

/// <summary>
/// <c>line message ...</c> — B. Message send. Message content is supplied via one of
/// <c>--text</c>, <c>--flex &lt;file&gt;</c>, or <c>--json &lt;file&gt;</c> (spec §4.2).
/// </summary>
internal sealed class MessageCommands
{
    private readonly CliRuntime _runtime;
    private readonly MessageService _messages;

    public MessageCommands(CliRuntime runtime, MessageService messages)
    {
        _runtime = runtime;
        _messages = messages;
    }

    [Command("push", Description = "Send a push message to a user/group/room.")]
    public Task<int> Push(GlobalOptions g, MessageInputOptions m,
        [Option("to", Description = "Destination id (userId/groupId/roomId).")] string to)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var credentials = _runtime.Resolve(g);
            var messagesJson = await m.ResolveMessagesJsonAsync(CancellationToken.None);
            var result = await _messages.PushRawAsync(credentials, to, messagesJson, CancellationToken.None);
            RenderSend(g, result);
        });
    }

    [Command("multicast", Description = "Send a message to multiple users.")]
    public Task<int> Multicast(GlobalOptions g, MessageInputOptions m,
        [Option("to", Description = "Comma-separated destination user ids.")] string to)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var credentials = _runtime.Resolve(g);
            var messagesJson = await m.ResolveMessagesJsonAsync(CancellationToken.None);
            var recipients = to.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var result = await _messages.MulticastAsync(credentials, recipients, messagesJson, CancellationToken.None);
            RenderSend(g, result);
        });
    }

    [Command("broadcast", Description = "Send a message to all friends.")]
    public Task<int> Broadcast(GlobalOptions g, MessageInputOptions m)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var credentials = _runtime.Resolve(g);
            var messagesJson = await m.ResolveMessagesJsonAsync(CancellationToken.None);
            var result = await _messages.BroadcastAsync(credentials, messagesJson, CancellationToken.None);
            RenderSend(g, result);
        });
    }

    [Command("reply", Description = "Reply to a webhook event using its reply token.")]
    public Task<int> Reply(GlobalOptions g, MessageInputOptions m,
        [Option("reply-token", Description = "Reply token from a webhook event.")] string replyToken)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var credentials = _runtime.Resolve(g);
            var messagesJson = await m.ResolveMessagesJsonAsync(CancellationToken.None);
            var result = await _messages.ReplyAsync(credentials, replyToken, messagesJson, CancellationToken.None);
            RenderSend(g, result);
        });
    }

    [Command("content", Description = "Download a message's binary content (image/video/audio/file).")]
    public Task<int> Content(GlobalOptions g,
        [Argument(Description = "Message id.")] string messageId,
        [Option('o', Description = "Output file path.")] string output)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var credentials = _runtime.Resolve(g);
            var result = await _messages.DownloadContentAsync(credentials, messageId, output, CancellationToken.None);
            if (g.Json)
            {
                Json.Print(result);
            }
            else
            {
                Console.WriteLine($"saved {result.Bytes} bytes to {result.Path}");
            }
        });
    }

    private static void RenderSend(GlobalOptions g, SendResult result)
    {
        if (g.Json)
        {
            Json.Print(result);
            return;
        }

        if (result.SentMessageIds.Count > 0)
        {
            Console.WriteLine($"sent ({result.SentMessageIds.Count}): {string.Join(", ", result.SentMessageIds)}");
        }
        else
        {
            Console.WriteLine("accepted.");
        }
    }
}

/// <summary>Mutually-exclusive message-content options shared by the send commands.</summary>
public sealed record MessageInputOptions(
    [Option("text", Description = "Send a single text message.")] string? Text = null,
    [Option("flex", Description = "Path to a Flex message contents JSON file.")] string? Flex = null,
    [Option("messages", Description = "Path to a JSON file containing the messages array.", ValueName = "FILE")] string? JsonFile = null,
    [Option("alt-text", Description = "Alt text for --flex (fallback for non-Flex clients).")] string AltText = "Flex message")
    : ICommandParameterSet
{
    /// <summary>Resolves the effective <c>messages</c> array JSON from whichever option was supplied.</summary>
    public async Task<string> ResolveMessagesJsonAsync(CancellationToken cancellationToken)
    {
        if (Text is not null)
        {
            return MessageJson.TextMessagesJson(Text);
        }

        if (Flex is not null)
        {
            var contents = await File.ReadAllTextAsync(Flex, cancellationToken).ConfigureAwait(false);
            return MessageJson.WrapFlex(contents, AltText);
        }

        if (JsonFile is not null)
        {
            return await File.ReadAllTextAsync(JsonFile, cancellationToken).ConfigureAwait(false);
        }

        throw new MessageInputException("Provide message content via --text, --flex <file>, or --messages <file>.");
    }
}
