using System.Linq;
using Line.OpenApi.Messaging;
using Microsoft.Extensions.AI;
using Xunit;

namespace Line.OpenApi.Extensions.AI.Tests;

// Tool-production gating and the "safety gates are not LLM-visible arguments" invariant (design sections 4-6).
public class ToolGatingTests
{
    private const string Push = "line_message_push";
    private const string Multicast = "line_message_multicast";
    private const string Reply = "line_message_reply";
    private const string Broadcast = "line_message_broadcast";
    private static readonly string[] SendTools = { Push, Multicast, Reply, Broadcast };
    private static readonly string[] ReadTools = { "line_bot_info", "line_bot_quota", "line_bot_profile", "line_message_validate" };

    private static MessagingClientHolder Client() => new();

    [Fact]
    public void ReadOnly_Produces_Only_Read_Tools()
    {
        using var holder = Client();
        var tools = LineMessagingAiTools.CreateReadOnly(holder.Client);

        var names = tools.Select(t => t.Name).ToHashSet();
        // Exact set: all four read tools present, and nothing else (no read tool silently dropped).
        Assert.Equal(ReadTools.ToHashSet(), names);
        Assert.DoesNotContain(names, n => SendTools.Contains(n));

        // Every tool must carry a non-empty description — it is the LLM's only interface to the tool.
        Assert.All(tools, t => Assert.False(string.IsNullOrWhiteSpace(t.Description)));
    }

    [Fact]
    public void Default_Options_Are_ReadOnly()
    {
        using var holder = Client();
        var tools = LineMessagingAiTools.Create(holder.Client);
        Assert.DoesNotContain(tools, t => SendTools.Contains(t.Name));
    }

    [Fact]
    public void EnableSending_Adds_Push_Multicast_Reply_But_Not_Broadcast()
    {
        using var holder = Client();
        var tools = LineMessagingAiTools.Create(holder.Client, new LineAiToolOptions { EnableSending = true });

        var names = tools.Select(t => t.Name).ToHashSet();
        Assert.Contains(Push, names);
        Assert.Contains(Multicast, names);
        Assert.Contains(Reply, names);
        Assert.DoesNotContain(Broadcast, names); // broadcast needs its own independent opt-in
    }

    [Fact]
    public void Broadcast_Requires_Its_Own_OptIn()
    {
        using var holder = Client();

        // AllowBroadcast alone (without EnableSending) must not produce the broadcast tool.
        var onlyAllow = LineMessagingAiTools.Create(holder.Client, new LineAiToolOptions { AllowBroadcast = true });
        Assert.DoesNotContain(onlyAllow, t => t.Name == Broadcast);

        // Both flags together produce it.
        var both = LineMessagingAiTools.Create(holder.Client, new LineAiToolOptions { EnableSending = true, AllowBroadcast = true });
        Assert.Contains(both, t => t.Name == Broadcast);
    }

    [Theory]
    [InlineData(Push, "to", "messagesJson")]
    [InlineData(Multicast, "to", "messagesJson")]
    [InlineData(Reply, "replyToken", "messagesJson")]
    [InlineData(Broadcast, "messagesJson")]
    public void Send_Tool_Schema_Has_Only_Its_Domain_Args(string toolName, params string[] expected)
    {
        using var holder = Client();
        var tools = LineMessagingAiTools.Create(holder.Client, new LineAiToolOptions
        {
            EnableSending = true,
            AllowBroadcast = true,
            DryRun = true,
            SendPolicy = (_, _) => new(true),
            BeforeSend = (_, _) => new(true),
        });

        var tool = tools.Single(t => t.Name == toolName);
        var props = tool.SchemaPropertyNames();

        Assert.Equal(expected.OrderBy(x => x), props.OrderBy(x => x));
    }

    [Fact]
    public void Safety_Gates_Never_Appear_As_Tool_Arguments()
    {
        using var holder = Client();
        var tools = LineMessagingAiTools.Create(holder.Client, new LineAiToolOptions
        {
            EnableSending = true,
            AllowBroadcast = true,
            DryRun = true,
            SendPolicy = (_, _) => new(true),
            BeforeSend = (_, _) => new(true),
        });

        // No tool's argument schema may expose a safety gate or the injected CancellationToken —
        // otherwise a model could flip a gate (design section 5.5).
        string[] forbidden =
        {
            "enableSending", "allowBroadcast", "dryRun", "sendPolicy", "beforeSend",
            "cancellationToken", "options", "client",
        };

        foreach (var tool in tools)
        {
            var props = tool.SchemaPropertyNames().Select(p => p.ToLowerInvariant()).ToHashSet();
            foreach (var bad in forbidden)
            {
                Assert.DoesNotContain(bad.ToLowerInvariant(), props);
            }
        }
    }

    // Holds a MessagingClient over an exploding transport: gating/schema tests never send, so any
    // network call here is a bug.
    private sealed class MessagingClientHolder : System.IDisposable
    {
        private readonly TestSupport.ExplodingHandler _handler = new();
        public MessagingClient Client { get; }
        public MessagingClientHolder() => Client = TestSupport.NewClient(_handler);
        public void Dispose() => _handler.Dispose();
    }
}
