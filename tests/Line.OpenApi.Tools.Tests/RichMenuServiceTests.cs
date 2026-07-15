using Line.OpenApi.Tools.Configuration;
using Line.OpenApi.Tools.Services;
using Xunit;

namespace Line.OpenApi.Tools.Tests;

/// <summary>
/// Verifies RichMenuService input handling. The JSON is parsed before any network call, so
/// malformed input is rejected as a MessageInputException (exit 2) without needing HTTP.
/// </summary>
public sealed class RichMenuServiceTests
{
    private static readonly ResolvedCredentials Credentials =
        new("default", ProfileExists: true, ChannelAccessToken: "token", ChannelId: null, ChannelSecret: null, PrivateKeyPath: null, Kid: null);

    [Fact]
    public async Task CreateAsync_RejectsMalformedJson_BeforeAnyNetworkCall()
        => await Assert.ThrowsAsync<MessageInputException>(() =>
            new RichMenuService().CreateAsync(Credentials, "{ not json", CancellationToken.None));

    [Fact]
    public async Task ValidateAsync_RejectsMalformedJson_BeforeAnyNetworkCall()
        => await Assert.ThrowsAsync<MessageInputException>(() =>
            new RichMenuService().ValidateAsync(Credentials, "{ not json", CancellationToken.None));
}
