using Line.OpenApi.Tools.Configuration;
using Line.OpenApi.Tools.Services;
using Xunit;

namespace Line.OpenApi.Tools.Tests;

/// <summary>
/// Verifies input handling for the coverage-package services (audience / shop). Request JSON is
/// parsed before any network call, so malformed or wrong-shape input is rejected as a
/// MessageInputException (exit 2) without needing HTTP. Mirrors RichMenuServiceTests.
/// </summary>
public sealed class CoverageServiceTests
{
    private static readonly ResolvedCredentials Credentials =
        new("default", ProfileExists: true, ChannelAccessToken: "token", ChannelId: null, ChannelSecret: null, PrivateKeyPath: null, Kid: null);

    [Fact]
    public async Task AudienceCreate_RejectsMalformedJson_BeforeAnyNetworkCall()
        => await Assert.ThrowsAsync<MessageInputException>(() =>
            new AudienceService().CreateAsync(Credentials, "{ not json", CancellationToken.None));

    [Fact]
    public async Task AudienceAddUsers_RejectsMalformedJson_BeforeAnyNetworkCall()
        => await Assert.ThrowsAsync<MessageInputException>(() =>
            new AudienceService().AddUsersAsync(Credentials, "{ not json", CancellationToken.None));

    [Fact]
    public async Task ShopMission_RejectsMalformedJson_BeforeAnyNetworkCall()
        => await Assert.ThrowsAsync<MessageInputException>(() =>
            new ShopService().SendMissionAsync(Credentials, "{ not json", CancellationToken.None));
}
