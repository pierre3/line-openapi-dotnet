using System.ClientModel;
using System.Text.Json;
using Line.OpenApi.Extensions.AI;
using Line.OpenApi.Messaging;
using Line.OpenApi.Samples.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Kiota.Abstractions.Authentication;
using OpenAI;

using Con = System.Console;

// LINE OpenApi .NET — AI tools sample (Line.OpenApi.Extensions.AI).
//
// Shows how to expose the LINE Messaging use case to an LLM as Microsoft.Extensions.AI tools, and
// how the safety gates (opt-in sending, an allow-list SendPolicy, a human-in-the-loop BeforeSend
// hook, and dry-run) behave when a model drives them.
//
// Two independent axes:
//   * The "brain": a deterministic ScriptedChatClient by default (no API key). Set LLM_MODEL +
//     LLM_API_KEY (and optionally LLM_BASE_URL for any OpenAI-compatible endpoint) to drive the
//     tools with a real model instead.
//   * The transport: a local stub by default (the safety gates run for real but nothing leaves the
//     machine). Set LINE_CHANNEL_ACCESS_TOKEN and pass `--send` to really deliver.

var token = Env("LINE_CHANNEL_ACCESS_TOKEN");
var allowedUser = Env("LINE_TO_USER_ID") ?? "Uallowed0000000000000000000000000";
const string blockedUser = "Ublocked0000000000000000000000000";

bool Flag(string name) => args.Contains(name, StringComparer.OrdinalIgnoreCase);

if (Flag("--help") || Flag("-h"))
{
    PrintUsage();
    return 0;
}

// Transport axis (independent of the gates): --send delivers for real, otherwise a local stub.
var wantSend = Flag("--send");
var live = token is not null && wantSend;

// Safety-gate axis — chosen here at startup by the developer/operator via flags (see --help). These
// are NOT tool arguments, so the model cannot change them regardless of how they are set; exposing
// them as command-line flags is just as safe as hard-coding them (a human sets both at startup).
var readOnly = Flag("--read-only");
var noPolicy = Flag("--no-policy");
var noApproval = Flag("--no-approval");
var allowBroadcast = Flag("--allow-broadcast");
var dryRun = Flag("--dry-run");

using var model = TryCreateModel(out var modelName);  // null => scripted brain

// The safety gates. All are set here, by the developer (from the flags above) — none is exposed as
// a tool argument, so the model cannot flip them whatever the flags are.
var options = new LineAiToolOptions
{
    EnableSending = !readOnly,                                          // --read-only turns sending off
    AllowBroadcast = !readOnly && allowBroadcast,                      // --allow-broadcast (needs sending)
    DryRun = dryRun,                                                   // --dry-run: validate only, no send
    SendPolicy = (!readOnly && !noPolicy) ? AllowListPolicy : null,    // --no-policy drops the allow-list
    BeforeSend = (!readOnly && !noApproval) ? ApproveOnConsole : null, // --no-approval drops the prompt
};

Con.WriteLine("==================================================");
Con.WriteLine(" LINE OpenApi .NET — AI tools sample");
Con.WriteLine($" Brain: {(model is not null ? $"LLM ({modelName})" : "SCRIPTED (deterministic, no API key)")}");
Con.WriteLine($" Send:  {(live ? "LIVE (will really send)" : "OFFLINE (local stub transport; gates run, no network)")}");
Con.WriteLine($" Gates: {DescribeGates(options)}");
Con.WriteLine(" (run with --help to change the gates)");
Con.WriteLine("==================================================\n");

// The caller builds the MessagingClient (the same client the non-AI library code uses). Live mode
// uses the real static token; offline mode uses a local stub transport so the gates still run but
// nothing leaves the machine.
var messagingClient = live
    ? MessagingClient.CreateWithStaticToken(token!)
    : new MessagingClient(new AnonymousAuthenticationProvider(), new HttpClient(new StubTransport()));

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
var textMessages = "[{\"type\":\"text\",\"text\":\"Meeting tomorrow at 10:00\"}]";
var validateResult = await tools.Single(t => t.Name == "line_message_validate")
    .InvokeAsync(new AIFunctionArguments { ["messagesJson"] = textMessages });
Con.WriteLine($"  -> {JsonSerializer.Serialize(validateResult)}\n");

// Part 2 — the model drives the tools through the gates -----------------------
if (model is not null)
{
    // Real LLM: let the model decide which tools to call. The gates run exactly the same.
    await RunReplAsync(model);
}
else
{
    // Scripted brain: fixed conversations that exercise the allow and deny paths. Which ones run
    // depends on the gate flags, so the demo stays honest about what the current gates actually do.
    if (!options.EnableSending)
    {
        Con.WriteLine("Sending is off (--read-only): only read/validate tools exist, so the send");
        Con.WriteLine("scenarios are skipped. Try line_message_validate above, or drop --read-only.\n");
    }
    else
    {
        // Allowed path: a push to the allow-listed user.
        await RunScriptedAsync(
            "Send the user a note that the meeting is at 10:00.",
            ScriptedChatClient.ToolCall("call-1", "line_message_push", new Dictionary<string, object?>
            {
                ["to"] = allowedUser,
                ["messagesJson"] = "[{\"type\":\"text\",\"text\":\"Reminder: meeting tomorrow at 10:00.\"}]",
            }),
            "Done — I sent the reminder.");

        // Denied path is only meaningful when a policy will actually be evaluated.
        if (options.SendPolicy is not null && !options.DryRun)
        {
            await RunScriptedAsync(
                "Now push the same reminder to an address that is not on the allow-list.",
                ScriptedChatClient.ToolCall("call-2", "line_message_push", new Dictionary<string, object?>
                {
                    ["to"] = blockedUser,
                    ["messagesJson"] = "[{\"type\":\"text\",\"text\":\"Reminder: meeting tomorrow at 10:00.\"}]",
                }),
                "I could not send it — the destination was refused by the send policy.");
        }
        else
        {
            var why = options.DryRun ? "--dry-run bypasses the policy" : "--no-policy removed the allow-list";
            Con.WriteLine($"(Deny scenario skipped: {why}, so no destination would be refused.)\n");
        }
    }

    Con.WriteLine("Done. Use --help to change the gates (--read-only / --no-policy / --no-approval /");
    Con.WriteLine("--allow-broadcast / --dry-run). Set LLM_MODEL + LLM_API_KEY to drive a real model, and");
    Con.WriteLine("LINE_CHANNEL_ACCESS_TOKEN + --send to deliver for real.");
}
return 0;

// Chat REPL backed by a real model. The model chooses when to call the LINE tools; every send still
// passes through SendPolicy + BeforeSend. Empty line exits.
async Task RunReplAsync(IChatClient chatModel)
{
    IChatClient agent = chatModel.AsBuilder().UseFunctionInvocation().Build();
    var chatOptions = new ChatOptions { Tools = [.. tools] };
    var history = new List<ChatMessage>
    {
        new(ChatRole.System,
            "You help operate a LINE bot with the provided tools. To message someone, call "
            + $"line_message_push. The only allow-listed recipient id is '{allowedUser}'. If a send "
            + "is refused, explain that to the user. Keep replies short."),
    };

    Con.WriteLine($"Chat with the LINE agent (allow-listed recipient: {allowedUser}). Empty line to quit.");
    Con.WriteLine($"Try: \"tell {allowedUser} the meeting is at 10:00\"\n");
    while (true)
    {
        Con.Write("You: ");
        var line = Con.ReadLine();
        if (string.IsNullOrWhiteSpace(line)) break;

        history.Add(new ChatMessage(ChatRole.User, line));
        var response = await agent.GetResponseAsync(history, chatOptions);
        Con.WriteLine($"Assistant: {response.Text}\n");
        history.AddMessages(response);
    }
}

// Builds a real IChatClient from LLM_* env vars, or returns null (scripted mode). Works with OpenAI
// and any OpenAI-compatible endpoint (LLM_BASE_URL), e.g. Groq / Together / Ollama / vLLM / LM Studio.
// The endpoint and model must support tool/function calling for the demo to work.
IChatClient? TryCreateModel(out string? name)
{
    name = Env("LLM_MODEL");
    var apiKey = Env("LLM_API_KEY");
    if (name is null || apiKey is null) return null;

    var clientOptions = new OpenAIClientOptions();
    var baseUrl = Env("LLM_BASE_URL");
    if (baseUrl is not null) clientOptions.Endpoint = new Uri(baseUrl);

    var openAi = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);
    return openAi.GetChatClient(name).AsIChatClient();
}

// Runs one scripted "conversation" through the real FunctionInvokingChatClient: the model requests a
// tool, M.E.AI invokes the LINE AIFunction (which runs the gates), then the model replies.
async Task RunScriptedAsync(string userPrompt, ChatMessage toolCallTurn, string finalTurn)
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
        // With the pinned M.E.AI version, FunctionInvokingChatClient catches the tool exception and
        // feeds an error result back to the model, so scenario 3 shows the scripted reply above
        // rather than this branch. This catch is a defensive fallback for versions/configurations
        // where a tool exception surfaces to the caller instead; either way no API call was made.
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

// A human-in-the-loop hook: show the pending send and ask for approval. In OFFLINE mode a
// non-interactive run (piped input) auto-approves so the sample completes unattended. In LIVE mode
// a real message would go out, so approval is never automatic — non-interactive runs are refused.
ValueTask<bool> ApproveOnConsole(LineSendContext ctx, CancellationToken ct)
{
    Con.WriteLine($"   [approve] about to {ctx.Operation} {ctx.MessagesJson}");
    if (Con.IsInputRedirected)
    {
        if (live)
        {
            Con.WriteLine("   [approve] non-interactive + LIVE -> refused (run interactively to approve a real send)");
            return new ValueTask<bool>(false);
        }
        Con.WriteLine("   [approve] non-interactive -> auto-approved (offline)");
        return new ValueTask<bool>(true);
    }
    Con.Write("   [approve] send it? [y/N] ");
    var yes = string.Equals(Con.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
    return new ValueTask<bool>(yes);
}

// One-line summary of the effective gate configuration for the banner.
static string DescribeGates(LineAiToolOptions o)
{
    if (!o.EnableSending) return "sending=OFF (read-only: only read/validate tools)";
    return string.Join("  ", new[]
    {
        "sending=ON",
        $"policy={(o.SendPolicy is not null ? "allow-list" : "none")}",
        $"approval={(o.BeforeSend is not null ? "console" : "none")}",
        $"broadcast={(o.AllowBroadcast ? "ON" : "OFF")}",
        $"dry-run={(o.DryRun ? "ON" : "OFF")}",
    });
}

// Usage / help text (printed for --help or -h). The gate flags below are set by the human at
// startup; they are never tool arguments, so the model cannot flip them however they are set.
static void PrintUsage()
{
    Con.WriteLine("LINE OpenApi .NET — AI tools sample\n");
    Con.WriteLine("Usage: dotnet run -- [options]\n");
    Con.WriteLine("Transport (independent of the safety gates):");
    Con.WriteLine("  --send             Deliver for real via the LINE API (needs LINE_CHANNEL_ACCESS_TOKEN).");
    Con.WriteLine("                     Without it, an offline stub transport is used and the gates still run.\n");
    Con.WriteLine("Safety gates (default: sending on, allow-list policy, console approval, broadcast off):");
    Con.WriteLine("  --read-only        Produce only read/validate tools; no send tool at all.");
    Con.WriteLine("  --no-policy        Drop the allow-list SendPolicy (any destination the model picks is allowed).");
    Con.WriteLine("  --no-approval      Drop the human-in-the-loop BeforeSend approval prompt.");
    Con.WriteLine("  --allow-broadcast  Also expose the broadcast tool (sends to every friend of the bot).");
    Con.WriteLine("  --dry-run          Send tools validate only and never contact the API (skips policy/approval).");
    Con.WriteLine("  -h, --help         Show this help.\n");
    Con.WriteLine("Environment:");
    Con.WriteLine("  LLM_MODEL + LLM_API_KEY (+ optional LLM_BASE_URL)  Drive a real model; unset = scripted brain.");
    Con.WriteLine("  LINE_TO_USER_ID                                   The allow-listed recipient userId.");
}

static string? Env(string name)
{
    var v = Environment.GetEnvironmentVariable(name);
    return string.IsNullOrWhiteSpace(v) ? null : v;
}
