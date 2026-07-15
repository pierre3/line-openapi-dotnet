using System;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Line.OpenApi.Login;

/// <summary>
/// A PKCE (Proof Key for Code Exchange) code verifier and its derived S256 challenge, produced
/// by <see cref="LineLoginSecurity.CreatePkceChallenge"/>. Send <see cref="CodeChallenge"/> (with
/// <see cref="CodeChallengeMethod"/>) on the authorization request, keep <see cref="CodeVerifier"/>
/// in the session, and pass it back to <c>ExchangeCodeAsync</c>.
/// </summary>
public sealed class PkceChallenge
{
    internal PkceChallenge(string codeVerifier, string codeChallenge)
    {
        CodeVerifier = codeVerifier;
        CodeChallenge = codeChallenge;
    }

    /// <summary>The high-entropy code verifier (43 characters, base64url). Keep it secret.</summary>
    public string CodeVerifier { get; }

    /// <summary>The code challenge derived from the verifier (base64url of its SHA-256 hash).</summary>
    public string CodeChallenge { get; }

    /// <summary>The challenge method. LINE supports only <c>S256</c>.</summary>
    public string CodeChallengeMethod => "S256";
}

/// <summary>
/// Helpers for the security parameters of the LINE Login authorization request: a CSRF
/// <c>state</c> value and a PKCE challenge. Both use a cryptographically secure random source.
/// </summary>
public static class LineLoginSecurity
{
    /// <summary>
    /// Generates a cryptographically random, URL-safe <c>state</c> value for CSRF protection.
    /// Store it in the session and compare it against the <c>state</c> returned on the callback.
    /// </summary>
    /// <param name="byteLength">Entropy in bytes (default 32 = 256 bits).</param>
    public static string GenerateState(int byteLength = 32)
    {
        if (byteLength <= 0) throw new ArgumentOutOfRangeException(nameof(byteLength));
        return Base64UrlEncode(RandomNumberGenerator.GetBytes(byteLength));
    }

    /// <summary>
    /// Creates a PKCE code verifier and its S256 code challenge. LINE supports only the
    /// <c>S256</c> method.
    /// </summary>
    public static PkceChallenge CreatePkceChallenge()
    {
        // 32 random bytes -> 43-char base64url verifier (within the RFC 7636 43-128 range).
        var verifier = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var challenge = Base64UrlEncode(challengeBytes);
        return new PkceChallenge(verifier, challenge);
    }

    private static string Base64UrlEncode(byte[] bytes) => Base64Url.EncodeToString(bytes);
}
