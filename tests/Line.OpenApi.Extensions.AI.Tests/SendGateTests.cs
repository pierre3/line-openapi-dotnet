using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Xunit;

namespace Line.OpenApi.Extensions.AI.Tests;

// Runtime send gating: DryRun / SendPolicy / BeforeSend, and the "no transport touched unless a
// send actually happens" invariant (design section 5).
public class SendGateTests
{
    private static AIFunction Tool(System.Collections.Generic.IReadOnlyList<AIFunction> tools, string name)
        => tools.Single(t => t.Name == name);

    [Fact]
    public async Task DryRun_Validates_Without_Touching_Transport()
    {
        var handler = new TestSupport.ExplodingHandler();
        var tools = LineMessagingAiTools.Create(TestSupport.NewClient(handler),
            new LineAiToolOptions { EnableSending = true, AllowBroadcast = true, DryRun = true });

        // Every send tool must short-circuit before the transport.
        await Tool(tools, "line_message_push").InvokeAsync(new AIFunctionArguments { ["to"] = "U1", ["messagesJson"] = TestSupport.OneText });
        await Tool(tools, "line_message_multicast").InvokeAsync(new AIFunctionArguments { ["to"] = new[] { "U1" }, ["messagesJson"] = TestSupport.OneText });
        await Tool(tools, "line_message_reply").InvokeAsync(new AIFunctionArguments { ["replyToken"] = "R1", ["messagesJson"] = TestSupport.OneText });
        await Tool(tools, "line_message_broadcast").InvokeAsync(new AIFunctionArguments { ["messagesJson"] = TestSupport.OneText });

        Assert.False(handler.WasCalled);
    }

    [Fact]
    public async Task SendPolicy_Deny_Refuses_Before_Transport()
    {
        var handler = new TestSupport.ExplodingHandler();
        var tools = LineMessagingAiTools.Create(TestSupport.NewClient(handler),
            new LineAiToolOptions { EnableSending = true, SendPolicy = (_, _) => new ValueTask<bool>(false) });

        var ex = await Assert.ThrowsAsync<LineSendRefusedException>(() =>
            Tool(tools, "line_message_push").InvokeAsync(
                new AIFunctionArguments { ["to"] = "U1", ["messagesJson"] = TestSupport.OneText }).AsTask());

        Assert.Equal(LineSendRefusalStage.Policy, ex.Stage);
        Assert.Equal(LineSendOperation.Push, ex.Context.Operation);
        Assert.False(handler.WasCalled);
    }

    [Fact]
    public async Task BeforeSend_Deny_Refuses_After_Policy_Before_Transport()
    {
        var handler = new TestSupport.ExplodingHandler();
        var policyEvaluated = false;
        var tools = LineMessagingAiTools.Create(TestSupport.NewClient(handler),
            new LineAiToolOptions
            {
                EnableSending = true,
                SendPolicy = (_, _) => { policyEvaluated = true; return new ValueTask<bool>(true); },
                BeforeSend = (_, _) => new ValueTask<bool>(false),
            });

        var ex = await Assert.ThrowsAsync<LineSendRefusedException>(() =>
            Tool(tools, "line_message_push").InvokeAsync(
                new AIFunctionArguments { ["to"] = "U1", ["messagesJson"] = TestSupport.OneText }).AsTask());

        Assert.True(policyEvaluated); // policy runs first
        Assert.Equal(LineSendRefusalStage.BeforeSend, ex.Stage);
        Assert.False(handler.WasCalled);
    }

    [Fact]
    public async Task SendPolicy_Sees_Multicast_Recipients_And_Count()
    {
        var handler = new TestSupport.RecordingHandler();
        LineSendContext? captured = null;
        var tools = LineMessagingAiTools.Create(TestSupport.NewClient(handler),
            new LineAiToolOptions { EnableSending = true, SendPolicy = (ctx, _) => { captured = ctx; return new ValueTask<bool>(true); } });

        await Tool(tools, "line_message_multicast").InvokeAsync(
            new AIFunctionArguments { ["to"] = new[] { "U1", "U2", "U3" }, ["messagesJson"] = TestSupport.OneText });

        Assert.NotNull(captured);
        Assert.Equal(LineSendOperation.Multicast, captured!.Operation);
        Assert.Equal(new[] { "U1", "U2", "U3" }, captured.Recipients);
        Assert.Equal(1, captured.MessageCount);
    }

    [Fact]
    public async Task SendPolicy_Can_Deny_Broadcast_By_Operation()
    {
        var handler = new TestSupport.ExplodingHandler();
        // A policy that only refuses broadcast (empty recipients would be ambiguous; Operation is not).
        var tools = LineMessagingAiTools.Create(TestSupport.NewClient(handler),
            new LineAiToolOptions
            {
                EnableSending = true,
                AllowBroadcast = true,
                SendPolicy = (ctx, _) => new ValueTask<bool>(ctx.Operation != LineSendOperation.Broadcast),
            });

        var ex = await Assert.ThrowsAsync<LineSendRefusedException>(() =>
            Tool(tools, "line_message_broadcast").InvokeAsync(
                new AIFunctionArguments { ["messagesJson"] = TestSupport.OneText }).AsTask());

        Assert.Equal(LineSendOperation.Broadcast, ex.Context.Operation);
        Assert.Empty(ex.Context.Recipients);
        Assert.False(handler.WasCalled);
    }

    [Fact]
    public async Task Push_HappyPath_Sends_Post_To_Push_Endpoint()
    {
        var handler = new TestSupport.RecordingHandler();
        var tools = LineMessagingAiTools.Create(TestSupport.NewClient(handler),
            new LineAiToolOptions { EnableSending = true });

        await Tool(tools, "line_message_push").InvokeAsync(
            new AIFunctionArguments { ["to"] = "U1", ["messagesJson"] = TestSupport.OneText });

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(System.Net.Http.HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.line.me/v2/bot/message/push", handler.Request.RequestUri!.ToString());
        Assert.Contains("\"U1\"", handler.RequestBody);
        Assert.Contains("hi", handler.RequestBody);
    }

    [Fact]
    public async Task Malformed_MessagesJson_Is_An_Input_Error()
    {
        var handler = new TestSupport.ExplodingHandler();
        var tools = LineMessagingAiTools.Create(TestSupport.NewClient(handler),
            new LineAiToolOptions { EnableSending = true });

        // MessageInputException is shared internal source, visible via InternalsVisibleTo.
        await Assert.ThrowsAsync<Line.OpenApi.Tools.Services.MessageInputException>(() =>
            Tool(tools, "line_message_push").InvokeAsync(
                new AIFunctionArguments { ["to"] = "U1", ["messagesJson"] = "{ not an array" }).AsTask());

        Assert.False(handler.WasCalled);
    }

    [Fact]
    public async Task Validate_Tool_Is_ReadOnly_And_Never_Sends()
    {
        var handler = new TestSupport.ExplodingHandler();
        var tools = LineMessagingAiTools.CreateReadOnly(TestSupport.NewClient(handler));

        var result = await Tool(tools, "line_message_validate").InvokeAsync(
            new AIFunctionArguments { ["messagesJson"] = TestSupport.OneText });

        Assert.NotNull(result);
        Assert.False(handler.WasCalled);
    }
}
