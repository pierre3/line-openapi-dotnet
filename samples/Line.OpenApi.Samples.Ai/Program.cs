using System.Text.Json;
using Line.OpenApi.Extensions.AI;
using Line.OpenApi.Messaging;
using Line.OpenApi.Samples.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Kiota.Abstractions.Authentication;

using Con = System.Console;

// LINE OpenApi .NET — AI tools sample (Line.OpenApi.Extensions.AI).
//
// Shows how to expose the LINE Messaging use case to an LLM as Microsoft.Extensions.AI tools, and
// how the safety gates (opt-in sending, an allow-list SendPolicy, a human-in-the-loop BeforeSend
// hook, and dry-run) behave when a model drives them.
//
// Offline by default: with no LINE_CHANNEL_ACCESS_TOKEN the Messaging client runs over a local stub
// transport (the safety gates run for real, but no request leaves the machine), and the "model" is
// a deterministic ScriptedChatClient — no API key required. To really send, set
// LINE_CHANNEL_ACCESS_TOKEN (and optionally LINE_TO_USER_ID) and pass `--send`.

var token = Env("LINE_CHANNEL_ACCESS_TOKEN");
var allowedUser = Env("LINE_TO_USER_ID") ?? "Uallowed0000000000000000000000000";
const string blockedUser = "Ublocked0000000000000000000000000";
var wantSend = args.Contains("--send", StringComparer.OrdinalIgnoreCase);
var live = token is not null && wantSend;

Con.WriteLine("==================================================");
Con.WriteLine(" LINE OpenApi .NET — AI tools sample");
Con.WriteLine($" Mode: {(live ? "LIVE (will really send)" : "OFFLINE (local stub transport; gates run, no network)")}");
Con.WriteLine("==================================================\n");

// The caller builds the MessagingClient (the same client the non-AI library code uses). Live mode
// uses the real static token; offline mode uses a local stub transport so the gates still run but
// nothing leaves the machine.
var messagingClient = live
    ? MessagingClient.CreateWithStaticToken(token!)
    : new MessagingClient(new AnonymousAuthenticationProvider(), new HttpClient(new StubTransport()));

// The safety gates. All are set here, by the developer — none is exposed as a tool argument, so the
// model cannot flip them.
var options = new LineAiToolOptions
{
    EnableSending = true,             // opt in to push/multicast/reply (broadcast stays off)
    SendPolicy = AllowListPolicy,     // structural gate: only allow-listed destinations
    BeforeSend = ApproveOnConsole,    // human-in-the-loop: inspect content before it goes out
};

var tools = LineMessagingAiTools.Create(messagingClient, options);

// Part 1 — what the model sees ------------------------------------------------
Con.WriteLine("Tools exposed to the model (safety gates are NOT among the arguments):");
foreach (var tool in tools)
{
    var props = tool.JsonSchema.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object
        ? string.Join(", ", p.EnumerateObject().Select(e => e.Name))
        : "(none)";
    Con.WriteLine($"  - {tool.Name}({props})");
}
Con.WriteLine();

// Direct invocation (how an M.E.AI host ultimately calls a tool) of the read-only validator.
Con.WriteLine("Directly invoking the read-only line_message_validate tool:");
var flex = "[{\"type\":\"text\",\"text\":\"Meeting tomorrow at 10:00\"}]";
var validateResult = await tools.Single(t => t.Name == "line_message_validate")
    .InvokeAsync(new AIFunctionArguments { ["messagesJson"] = flex });
Con.WriteLine($"  -> {JsonSerializer.Serialize(validateResult)}\n");

// Part 2 — the model drives the tools through the gates -----------------------
await RunAgentAsync(
    "Send the user a note that the meeting is at 10:00.",
    ScriptedChatClient.ToolCall("call-1", "line_message_push", new Dictionary<string, object?>
    {
        ["to"] = allowedUser,
        ["messagesJson"] = "[{\"type\":\"text\",\"text\":\"Reminder: meeting tomorrow at 10:00.\"}]",
    }),
    "Done — I sent the reminder.");

await RunAgentAsync(
    "Now push the same reminder to an address that is not on the allow-list.",
    ScriptedChatClient.ToolCall("call-2", "line_message_push", new Dictionary<string, object?>
    {
        ["to"] = blockedUser,
        ["messagesJson"] = "[{\"type\":\"text\",\"text\":\"Reminder: meeting tomorrow at 10:00.\"}]",
    }),
    "I could not send it — the destination was refused by the send policy.");

Con.WriteLine("Done. Re-run with LINE_CHANNEL_ACCESS_TOKEN set and `--send` to deliver for real.");
return 0;

// Runs one scripted "conversation" through the real FunctionInvokingChatClient: the model requests a
// tool, M.E.AI invokes the LINE AIFunction (which runs the gates), then the model replies.
async Task RunAgentAsync(string userPrompt, ChatMessage toolCallTurn, string finalTurn)
{
    Con.WriteLine($"── User: {userPrompt}");

    IChatClient agent = new ScriptedChatClient(new[] { toolCallTurn, ScriptedChatClient.FinalText(finalTurn) })
        .AsBuilder()
        .UseFunctionInvocation()
        .Build();

    var chatOptions = new ChatOptions { Tools = [.. tools] };
    try
    {
        var response = await agent.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, userPrompt) }, chatOptions);
        Con.WriteLine($"── Assistant: {response.Text}\n");
    }
    catch (LineSendRefusedException ex)
    {
        // Depending on the M.E.AI version a tool exception may surface here rather than being fed
        // back to the model; either way the send was blocked before any API call.
        Con.WriteLine($"── [refused] {ex.Message}\n");
    }
}

// A SendPolicy that permits only allow-listed destinations. Broadcast (no recipients) is refused.
ValueTask<bool> AllowListPolicy(LineSendContext ctx, CancellationToken ct)
{
    var ok = ctx.Operation != LineSendOperation.Broadcast
        && ctx.Recipients.Count > 0
        && ctx.Recipients.All(r => r == allowedUser);
    Con.WriteLine($"   [policy] {ctx.Operation} to [{string.Join(", ", ctx.Recipients)}] x{ctx.MessageCount} -> {(ok ? "ALLOW" : "DENY")}");
    return new ValueTask<bool>(ok);
}

// A human-in-the-loop hook: show the pending send and ask for approval. Non-interactive runs
// (piped input) auto-approve so the sample completes unattended.
ValueTask<bool> ApproveOnConsole(LineSendContext ctx, CancellationToken ct)
{
    Con.WriteLine($"   [approve] about to {ctx.Operation} {ctx.MessagesJson}");
    if (Con.IsInputRedirected)
    {
        Con.WriteLine("   [approve] non-interactive -> auto-approved");
        return new ValueTask<bool>(true);
    }
    Con.Write("   [approve] send it? [y/N] ");
    var yes = string.Equals(Con.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
    return new ValueTask<bool>(yes);
}

static string? Env(string name)
{
    var v = Environment.GetEnvironmentVariable(name);
    return string.IsNullOrWhiteSpace(v) ? null : v;
}
