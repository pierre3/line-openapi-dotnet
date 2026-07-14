using Line.OpenApi.Tools.Mcp;
using Line.OpenApi.Tools.Services;
using Xunit;

namespace Line.OpenApi.Tools.Tests;

/// <summary>
/// Guards the security-critical invariant (spec §4.5): <c>line_token_issue</c> over MCP never
/// returns the raw token unless the caller asked (reveal) AND the server allows it.
/// </summary>
public sealed class TokenIssueRevealGateTests
{
    private static readonly TokenIssueResult Issued =
        new("secret-token-value-1234", TimeSpan.FromDays(30), TokenKind.V21, "kid-1");

    [Fact]
    public void Default_does_not_return_raw_token_and_returns_metadata()
    {
        var r = WriteTools.BuildIssueResponse(Issued, "default", reveal: false, allowSecretOutput: false);

        Assert.Null(r.AccessToken);
        Assert.Null(r.RevealDenied);
        Assert.Equal("default", r.StoredProfile);
        Assert.Equal("…1234", r.MaskedToken);
        Assert.Equal("V21", r.TokenType);
        Assert.Equal(30 * 24 * 3600, r.ExpiresInSeconds);
    }

    [Fact]
    public void Reveal_without_server_allow_is_denied_and_token_withheld()
    {
        var r = WriteTools.BuildIssueResponse(Issued, "default", reveal: true, allowSecretOutput: false);

        Assert.Null(r.AccessToken);
        Assert.NotNull(r.RevealDenied);
    }

    [Fact]
    public void Server_allow_without_reveal_still_withholds_token()
    {
        var r = WriteTools.BuildIssueResponse(Issued, "default", reveal: false, allowSecretOutput: true);

        Assert.Null(r.AccessToken);
        Assert.Null(r.RevealDenied);
    }

    [Fact]
    public void Reveal_with_server_allow_returns_raw_token()
    {
        var r = WriteTools.BuildIssueResponse(Issued, "default", reveal: true, allowSecretOutput: true);

        Assert.Equal("secret-token-value-1234", r.AccessToken);
        Assert.Null(r.RevealDenied);
    }
}
