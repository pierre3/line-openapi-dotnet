using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Xunit;

namespace Line.OpenApi.Extensions.AI.Tests;

// Transport-level positives (read-tool mapping, non-push send endpoints) and send-context correctness
// that complement ToolGatingTests / SendGateTests.
public class TransportAndContextTests
{
    private static AIFunction Tool(System.Collections.Generic.IReadOnlyList<AIFunction> tools, string name)
        => tools.Single(t => t.Name == name);

    private static TestSupport.RecordingHandler Json(string body)
        => new(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

    private static string Render(object? result) => JsonSerializer.Serialize(result);

    [Fact]
    public async Task BotInfo_Gets_Info_Endpoint()
    {
        var handler = Json("{\"userId\":\"U1\",\"basicId\":\"@bot\",\"displayName\":\"MyBot\",\"chatMode\":\"chat\"}");
        var tools = LineMessagingAiTools.CreateReadOnly(TestSupport.NewClient(handler));

        var result = await Tool(tools, "line_bot_info").InvokeAsync();

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal("https://api.line.me/v2/bot/info", handler.Request.RequestUri!.ToString());
        Assert.Contains("MyBot", Render(result)); // response field flowed through to the flat DTO
    }

    [Fact]
    public async Task Profile_Gets_Profile_Endpoint_And_Maps_Fields()
    {
        var handler = Json("{\"userId\":\"U1\",\"displayName\":\"Alice\",\"language\":\"en\"}");
        var tools = LineMessagingAiTools.CreateReadOnly(TestSupport.NewClient(handler));

        var result = await Tool(tools, "line_bot_profile").InvokeAsync(new AIFunctionArguments { ["userId"] = "U1" });

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal("https://api.line.me/v2/bot/profile/U1", handler.Request.RequestUri!.ToString());
        var json = Render(result);
        Assert.Contains("Alice", json);
        Assert.Contains("en", json);
    }

    [Fact]
    public async Task Quota_Gets_Quota_Endpoint()
    {
        var handler = Json("{\"type\":\"limited\",\"value\":500}");
        var tools = LineMessagingAiTools.CreateReadOnly(TestSupport.NewClient(handler));

        await Tool(tools, "line_bot_quota").InvokeAsync();

        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
        Assert.Equal("https://api.line.me/v2/bot/message/quota", handler.Request.RequestUri!.ToString());
    }

    [Theory]
    [InlineData("line_message_multicast", "https://api.line.me/v2/bot/message/multicast")]
    [InlineData("line_message_reply", "https://api.line.me/v2/bot/message/reply")]
    [InlineData("line_message_broadcast", "https://api.line.me/v2/bot/message/broadcast")]
    public async Task Empty_Body_Sends_Post_To_Endpoint(string tool, string url)
    {
        var handler = new TestSupport.RecordingHandler(); // default 200 "{}" (empty-ish body)
        var tools = LineMessagingAiTools.Create(TestSupport.NewClient(handler),
            new LineAiToolOptions { EnableSending = true, AllowBroadcast = true });

        AIFunctionArguments args = tool switch
        {
            "line_message_multicast" => new() { ["to"] = new[] { "U1" }, ["messagesJson"] = TestSupport.OneText },
            "line_message_reply" => new() { ["replyToken"] = "R1", ["messagesJson"] = TestSupport.OneText },
            _ => new() { ["messagesJson"] = TestSupport.OneText },
        };

        await Tool(tools, tool).InvokeAsync(args);

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(url, handler.Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Push_SendPolicy_Sees_Single_Recipient()
    {
        var handler = new TestSupport.RecordingHandler();
        LineSendContext? captured = null;
        var tools = LineMessagingAiTools.Create(TestSupport.NewClient(handler),
            new LineAiToolOptions { EnableSending = true, SendPolicy = (ctx, _) => { captured = ctx; return new ValueTask<bool>(true); } });

        await Tool(tools, "line_message_push").InvokeAsync(
            new AIFunctionArguments { ["to"] = "U1", ["messagesJson"] = TestSupport.OneText });

        Assert.NotNull(captured);
        Assert.Equal(LineSendOperation.Push, captured!.Operation);
        Assert.Equal(new[] { "U1" }, captured.Recipients);
        Assert.Equal(1, captured.MessageCount);
    }

    [Fact]
    public async Task DryRun_Short_Circuits_Before_The_Gates()
    {
        var handler = new TestSupport.ExplodingHandler();
        var policyRan = false;
        var beforeSendRan = false;
        var tools = LineMessagingAiTools.Create(TestSupport.NewClient(handler),
            new LineAiToolOptions
            {
                EnableSending = true,
                DryRun = true,
                SendPolicy = (_, _) => { policyRan = true; return new ValueTask<bool>(true); },
                BeforeSend = (_, _) => { beforeSendRan = true; return new ValueTask<bool>(true); },
            });

        var result = await Tool(tools, "line_message_push").InvokeAsync(
            new AIFunctionArguments { ["to"] = "U1", ["messagesJson"] = TestSupport.OneText });

        // DryRun returns validation without evaluating the gates or touching the transport.
        Assert.False(policyRan);
        Assert.False(beforeSendRan);
        Assert.False(handler.WasCalled);
        Assert.Contains("\"count\"", Render(result).ToLowerInvariant()); // a validation result, not a send result
    }
}
