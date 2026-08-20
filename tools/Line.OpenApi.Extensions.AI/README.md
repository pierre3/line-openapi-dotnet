**English** | [日本語](https://github.com/pierre3/line-openapi-dotnet/blob/main/tools/Line.OpenApi.Extensions.AI/README_ja.md)

# Line.OpenApi.Extensions.AI — LINE messaging tools for LLM tool-calling

[![NuGet](https://img.shields.io/nuget/v/Line.OpenApi.Extensions.AI.svg)](https://www.nuget.org/packages/Line.OpenApi.Extensions.AI)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/pierre3/line-openapi-dotnet/blob/main/LICENSE)

Exposes the LINE **Messaging** use case as in-process [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/) `AIFunction` tools, so an AI agent built on Semantic Kernel or any Microsoft.Extensions.AI host can operate a LINE bot — send messages, look up bot info / quota / a user profile, or validate a message payload — by calling tools.

It complements the out-of-process [`Line.OpenApi.Tools`](https://www.nuget.org/packages/Line.OpenApi.Tools) MCP server: use the MCP server for an external agent (Claude Desktop / Claude Code), and this package when you are building your **own** .NET agent and want the tools **in-process**, with no separate process.

- **Safe by default** — read-only unless you explicitly opt in to sending.
- **Gated sending** — a send policy and a human-in-the-loop hook, both set by you and never visible to the model.
- **Tiny dependency surface** — depends only on `Line.OpenApi.Messaging` and `Microsoft.Extensions.AI.Abstractions`. No implementation / DI packages are pulled in.

Target framework: **`net10.0`**.

## Installation

```sh
dotnet add package Line.OpenApi.Extensions.AI
```

## Quick start

```csharp
using Line.OpenApi.Extensions.AI;
using Line.OpenApi.Messaging;
using Microsoft.Extensions.AI;

// The caller builds the MessagingClient (the same client the non-AI library code uses).
var line = MessagingClient.CreateWithStaticToken("CHANNEL_ACCESS_TOKEN");

// Safe by default: read-only tools only (bot info / quota / profile / message-validate).
IReadOnlyList<AIFunction> tools = LineMessagingAiTools.CreateReadOnly(line);

// Hand them to any Microsoft.Extensions.AI chat client:
var chatOptions = new ChatOptions { Tools = [.. tools] };
IChatClient agent = chatClient.AsBuilder().UseFunctionInvocation().Build();
var response = await agent.GetResponseAsync("How many messages can I still send this month?", chatOptions);
```

To let the model **send**, opt in explicitly and add your gates:

```csharp
IReadOnlyList<AIFunction> tools = LineMessagingAiTools.Create(line, new LineAiToolOptions
{
    EnableSending  = true,                 // enables push / multicast / reply (default false)
    AllowBroadcast = false,                // broadcast = largest blast radius; separate opt-in

    // Structural gate: bound the blast radius (operation / recipients / count). Return false to refuse.
    SendPolicy = (ctx, ct) => new(
        ctx.Operation != LineSendOperation.Broadcast &&
        ctx.Recipients.All(id => myAllowList.Contains(id))),

    // Human-in-the-loop / audit: inspect the actual content before it goes out. Return false to refuse.
    BeforeSend = async (ctx, ct) =>
    {
        Console.WriteLine($"About to {ctx.Operation}: {ctx.MessagesJson}");
        return await AskForApprovalAsync(ct);
    },
});
```

## Safety model

All safety gates are set by **you** (the developer) when the tools are created — via [`LineAiToolOptions`](https://github.com/pierre3/line-openapi-dotnet/blob/main/tools/Line.OpenApi.Extensions.AI/LineAiToolOptions.cs). **None of them is exposed as a tool argument**, so a model — even one following a prompt-injected instruction — cannot flip them through a tool call.

| Gate | Default | Effect |
|---|---|---|
| `EnableSending` | `false` | Produces the send tools (`push` / `multicast` / `reply`). Off → read-only toolset. |
| `AllowBroadcast` | `false` | Also produces `broadcast` (sends to every friend). Requires `EnableSending`. |
| `SendPolicy` | `null` | Evaluated before every send to bound blast radius. Return `false` to refuse. |
| `BeforeSend` | `null` | Human-in-the-loop / audit hook after the policy. The place to review message **content**. |
| `DryRun` | `false` | Send tools validate the payload only and never contact the API (skips policy / approval). |

A refused send never reaches the API and surfaces a `LineSendRefusedException`.

## Tools

The read / validate tools are always produced; the send tools are produced only when the matching gate is set. The **Arguments** column is the *whole* argument list a model sees — the safety gates are not among them.

| Tool | Arguments | Kind | Produced when |
|---|---|---|---|
| `line_bot_info` | *(none)* | read | Always |
| `line_bot_quota` | *(none)* | read | Always |
| `line_bot_profile` | `userId` | read | Always |
| `line_message_validate` | `messagesJson` | validate (never sends) | Always |
| `line_message_push` | `to`, `messagesJson` | send | `EnableSending = true` |
| `line_message_multicast` | `to`, `messagesJson` | send | `EnableSending = true` |
| `line_message_reply` | `replyToken`, `messagesJson` | send | `EnableSending = true` |
| `line_message_broadcast` | `messagesJson` | send (all friends) | `EnableSending = true` **and** `AllowBroadcast = true` |

Tool names mirror the [`Line.OpenApi.Tools`](https://www.nuget.org/packages/Line.OpenApi.Tools) MCP tools. `messagesJson` is a JSON array of LINE message objects (1–5), the same shape the Messaging API accepts.

## Semantic Kernel

The tools are plain `AIFunction`s, so Semantic Kernel consumes them directly:

```csharp
kernel.Plugins.AddFromFunctions("Line", tools);
```

## Notes

- **Only the Abstractions are referenced.** `AIFunctionFactory` lives in `Microsoft.Extensions.AI.Abstractions`; this package does not pull the implementation / DI packages. Bring your own `IChatClient` provider.
- **Content flows to your LLM provider.** Message content and read-tool results are sent to whatever chat client you wire up, and `ctx.MessagesJson` is passed to your gates — treat tool arguments as potential PII in your logs.
- **Rate / cumulative-count limiting** is the host pipeline's responsibility, not this package's.
- **Released on its own cadence** (tag `ai-v*`), separate from the client libraries (`v*`) and the CLI / MCP tool (`tools-v*`).

## Example

For a runnable end-to-end example — a scripted **or** a real model driving the tools through the gates, offline by default — see [`samples/Line.OpenApi.Samples.Ai`](https://github.com/pierre3/line-openapi-dotnet/blob/main/samples/README.md#4-ai-tools-agent-lineopenapisamplesai).

## Related documentation

- Repository & client libraries: [`README.md`](https://github.com/pierre3/line-openapi-dotnet/blob/main/README.md)
- CLI / MCP tool: [`Line.OpenApi.Tools`](https://www.nuget.org/packages/Line.OpenApi.Tools) ([docs](https://github.com/pierre3/line-openapi-dotnet/blob/main/tools/README.md))
- Design: [`docs/LINE-dotnet-AI-plugin-design.md`](https://github.com/pierre3/line-openapi-dotnet/blob/main/docs/LINE-dotnet-AI-plugin-design.md)

## License

MIT (see [`LICENSE`](https://github.com/pierre3/line-openapi-dotnet/blob/main/LICENSE) at the repository root).
