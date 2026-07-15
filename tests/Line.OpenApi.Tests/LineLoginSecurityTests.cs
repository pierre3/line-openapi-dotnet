using System;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Line.OpenApi.Login;
using Xunit;

namespace Line.OpenApi.Tests;

// Verifies PKCE and state generation: URL-safe output, correct verifier->challenge derivation,
// and randomness between calls.
public class LineLoginSecurityTests
{
    [Fact]
    public void GenerateState_IsUrlSafe_And_NonEmpty()
    {
        var state = LineLoginSecurity.GenerateState();
        Assert.False(string.IsNullOrEmpty(state));
        Assert.DoesNotContain('+', state);
        Assert.DoesNotContain('/', state);
        Assert.DoesNotContain('=', state);
    }

    [Fact]
    public void GenerateState_ProducesDifferentValues()
        => Assert.NotEqual(LineLoginSecurity.GenerateState(), LineLoginSecurity.GenerateState());

    [Fact]
    public void GenerateState_Rejects_NonPositiveLength()
        => Assert.Throws<ArgumentOutOfRangeException>(() => LineLoginSecurity.GenerateState(0));

    [Fact]
    public void CreatePkceChallenge_UsesS256_And_UrlSafeVerifier()
    {
        var pkce = LineLoginSecurity.CreatePkceChallenge();

        Assert.Equal("S256", pkce.CodeChallengeMethod);
        Assert.Equal(43, pkce.CodeVerifier.Length); // 32 random bytes -> 43 base64url chars
        foreach (var s in new[] { pkce.CodeVerifier, pkce.CodeChallenge })
        {
            Assert.DoesNotContain('+', s);
            Assert.DoesNotContain('/', s);
            Assert.DoesNotContain('=', s);
        }
    }

    [Fact]
    public void CreatePkceChallenge_Derives_ChallengeFromVerifier()
    {
        var pkce = LineLoginSecurity.CreatePkceChallenge();

        // challenge == base64url( SHA256( ASCII(verifier) ) )
        var expected = Base64Url.EncodeToString(
            SHA256.HashData(Encoding.ASCII.GetBytes(pkce.CodeVerifier)));
        Assert.Equal(expected, pkce.CodeChallenge);
    }

    [Fact]
    public void CreatePkceChallenge_ProducesDifferentVerifiers()
        => Assert.NotEqual(
            LineLoginSecurity.CreatePkceChallenge().CodeVerifier,
            LineLoginSecurity.CreatePkceChallenge().CodeVerifier);
}
