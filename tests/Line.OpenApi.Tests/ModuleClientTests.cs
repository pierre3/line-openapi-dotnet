using System;
using System.Threading.Tasks;
using Line.OpenApi.Module;
using Line.OpenApi.Module.Generated.Models;
using Xunit;

namespace Line.OpenApi.Tests;

// Path verification for the ModuleClient facade against the single host (api.line.me).
// The HTTP path (body, query) is verified separately in ModuleClientHttpTests.
public class ModuleClientTests
{
    [Fact]
    public void GetModules_BuildsGet_ToApiLineMe()
    {
        var client = ModuleClient.CreateWithStaticToken("TOKEN");

        var req = client.Api.V2.Bot.List.ToGetRequestInformation();

        Assert.Equal("api.line.me", req.URI.Host);
        Assert.Equal("/v2/bot/list", req.URI.AbsolutePath);
    }

    [Fact]
    public void AcquireChatControl_BuildsPost_WithChatId()
    {
        var client = ModuleClient.CreateWithStaticToken("TOKEN");

        var req = client.Api.V2.Bot.Chat["chat-1"].Control.Acquire
            .ToPostRequestInformation(new AcquireChatControlRequest());

        Assert.Equal("api.line.me", req.URI.Host);
        Assert.Equal("/v2/bot/chat/chat-1/control/acquire", req.URI.AbsolutePath);
    }

    // --- Argument guards ---

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task AcquireChatControlAsync_MissingChatId_Throws(string? chatId)
    {
        var client = ModuleClient.CreateWithStaticToken("TOKEN");
        await Assert.ThrowsAsync<ArgumentException>(
            () => client.AcquireChatControlAsync(chatId!, new AcquireChatControlRequest()));
    }

    [Fact]
    public async Task AcquireChatControlAsync_NullRequest_Throws()
    {
        var client = ModuleClient.CreateWithStaticToken("TOKEN");
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => client.AcquireChatControlAsync("chat-1", null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task ReleaseChatControlAsync_MissingChatId_Throws(string? chatId)
    {
        var client = ModuleClient.CreateWithStaticToken("TOKEN");
        await Assert.ThrowsAsync<ArgumentException>(() => client.ReleaseChatControlAsync(chatId!));
    }

    [Fact]
    public async Task DetachAsync_NullRequest_Throws()
    {
        var client = ModuleClient.CreateWithStaticToken("TOKEN");
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.DetachAsync(null!));
    }
}
