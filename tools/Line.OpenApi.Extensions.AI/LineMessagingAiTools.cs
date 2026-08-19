using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Line.OpenApi.Messaging;
using Line.OpenApi.Tools.Services; // shared source: internal flat DTOs used as delegate return types
using Microsoft.Extensions.AI;

namespace Line.OpenApi.Extensions.AI;

/// <summary>
/// Builds <see cref="AIFunction"/> tools that let an LLM (Semantic Kernel or any
/// Microsoft.Extensions.AI host) operate the LINE Messaging API. Tool names mirror the CLI/MCP tools
/// (<c>line_message_push</c>, <c>line_bot_profile</c>, ...).
///
/// Safe by default: <see cref="CreateReadOnly"/> (and <see cref="Create"/> with no send opt-in)
/// returns only read/validate tools. Sending requires <see cref="LineAiToolOptions.EnableSending"/>,
/// broadcast additionally requires <see cref="LineAiToolOptions.AllowBroadcast"/>, and every send is
/// gated by the developer-set <see cref="LineAiToolOptions.SendPolicy"/> /
/// <see cref="LineAiToolOptions.BeforeSend"/>. None of those gates is a tool argument, so a model
/// cannot flip them (design sections 4-5).
///
/// Usage:
///   var tools = LineMessagingAiTools.Create(messagingClient, new LineAiToolOptions { EnableSending = true });
///   kernel.Plugins.AddFromFunctions("Line", tools); // Semantic Kernel consumes M.E.AI functions
/// </summary>
public static class LineMessagingAiTools
{
    /// <summary>
    /// Creates the read-only toolset (bot info / quota / profile / message-validate). No send tool is
    /// produced. Equivalent to <see cref="Create"/> with default options.
    /// </summary>
    /// <param name="client">The Messaging client the tools operate through (built by the caller / DI).</param>
    public static IReadOnlyList<AIFunction> CreateReadOnly(MessagingClient client)
        => Create(client, new LineAiToolOptions());

    /// <summary>
    /// Creates the toolset for the given options. Read/validate tools are always included; send tools
    /// are included only when <see cref="LineAiToolOptions.EnableSending"/> is set, and the broadcast
    /// tool only when <see cref="LineAiToolOptions.AllowBroadcast"/> is also set.
    /// </summary>
    /// <param name="client">The Messaging client the tools operate through (built by the caller / DI).</param>
    /// <param name="options">Tool-production and send-gating options. When null, a read-only toolset is produced.</param>
    public static IReadOnlyList<AIFunction> Create(MessagingClient client, LineAiToolOptions? options = null)
    {
        if (client is null) throw new ArgumentNullException(nameof(client));
        options ??= new LineAiToolOptions();

        var toolset = new LineMessagingToolset(client, options);
        var tools = new List<AIFunction>
        {
            // Read-only tools — always available and safe.
            AIFunctionFactory.Create(
                (Func<CancellationToken, Task<BotInfo>>)toolset.GetBotInfoAsync, name: "line_bot_info"),
            AIFunctionFactory.Create(
                (Func<CancellationToken, Task<QuotaInfo>>)toolset.GetQuotaAsync, name: "line_bot_quota"),
            AIFunctionFactory.Create(
                (Func<string, CancellationToken, Task<ProfileInfo>>)toolset.GetProfileAsync, name: "line_bot_profile"),
            AIFunctionFactory.Create(
                (Func<string, CancellationToken, Task<MessageValidationResult>>)toolset.ValidateMessagesAsync, name: "line_message_validate"),
        };

        if (options.EnableSending)
        {
            tools.Add(AIFunctionFactory.Create(
                (Func<string, string, CancellationToken, Task<object>>)toolset.PushAsync, name: "line_message_push"));
            tools.Add(AIFunctionFactory.Create(
                (Func<string[], string, CancellationToken, Task<object>>)toolset.MulticastAsync, name: "line_message_multicast"));
            tools.Add(AIFunctionFactory.Create(
                (Func<string, string, CancellationToken, Task<object>>)toolset.ReplyAsync, name: "line_message_reply"));

            // Broadcast is the largest blast radius, so it needs its own independent opt-in.
            if (options.AllowBroadcast)
            {
                tools.Add(AIFunctionFactory.Create(
                    (Func<string, CancellationToken, Task<object>>)toolset.BroadcastAsync, name: "line_message_broadcast"));
            }
        }

        return tools;
    }
}
