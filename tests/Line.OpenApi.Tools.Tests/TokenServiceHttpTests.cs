using System.Net;
using Line.OpenApi.Tools.Services;
using Microsoft.Kiota.Abstractions;
using Xunit;

namespace Line.OpenApi.Tools.Tests;

/// <summary>
/// Exercises the CLI-owned token network paths (self-wired generated client) that the library's
/// own token-source tests do not cover: verify's 400↔other status handling.
/// </summary>
public sealed class TokenServiceHttpTests
{
    [Fact]
    public async Task VerifyAsync_200_returns_valid_with_metadata()
    {
        var body = "{\"client_id\":\"1234\",\"expires_in\":2592000,\"scope\":\"profile\"}";
        var service = new TokenService(new StubHttpMessageHandler(HttpStatusCode.OK, body));

        var result = await service.VerifyAsync("some-token", CancellationToken.None);

        Assert.True(result.Valid);
        Assert.Equal(2592000, result.ExpiresInSeconds);
        Assert.Equal("1234", result.ClientId);
    }

    [Fact]
    public async Task VerifyAsync_400_returns_invalid()
    {
        var service = new TokenService(new StubHttpMessageHandler(HttpStatusCode.BadRequest,
            "{\"error\":\"invalid_request\"}"));

        var result = await service.VerifyAsync("bad-token", CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Null(result.ExpiresInSeconds);
    }

    [Fact]
    public async Task VerifyAsync_500_propagates_as_api_error()
    {
        // A server-side failure must NOT be misreported as "invalid token"; it propagates
        // (mapped to exit 4 by CliRuntime).
        var service = new TokenService(new StubHttpMessageHandler(HttpStatusCode.InternalServerError));

        await Assert.ThrowsAsync<ApiException>(() => service.VerifyAsync("t", CancellationToken.None));
    }
}
