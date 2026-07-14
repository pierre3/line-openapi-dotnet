using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Line.OpenApi.Cli.Services;

/// <summary>
/// Builds the signed JWT client assertion required to issue a channel access token.
/// Signing is application-specific — the client library never touches the channel's
/// private key and provides no signer — so this lives in the CLI (spec §4.1, Medium②).
///
/// Header <c>{ alg: RS256, typ: JWT, kid }</c>, payload
/// <c>{ iss, sub, aud: "https://api.line.me/", exp, token_exp }</c>, signed with
/// RSASSA-PKCS1-v1_5 over SHA-256.
/// </summary>
internal static class JwtAssertionBuilder
{
    /// <param name="channelId">The channel id (used as both issuer and subject).</param>
    /// <param name="kid">The key id (JWK "kid") registered for the assertion signing key.</param>
    /// <param name="privateKeyPem">The RSA private key in PEM form.</param>
    /// <param name="tokenLifetime">Requested lifetime of the issued token (max 30 days).</param>
    public static string Build(string channelId, string kid, string privateKeyPem, TimeSpan tokenLifetime)
    {
        var now = DateTimeOffset.UtcNow;

        var header = new { alg = "RS256", typ = "JWT", kid };
        var payload = new
        {
            iss = channelId,
            sub = channelId,
            aud = "https://api.line.me/",
            exp = now.AddMinutes(30).ToUnixTimeSeconds(), // assertion validity (short)
            token_exp = (long)tokenLifetime.TotalSeconds,  // requested access-token lifetime
        };

        var signingInput =
            $"{Base64Url(JsonSerializer.SerializeToUtf8Bytes(header))}." +
            $"{Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload))}";

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var signature = rsa.SignData(
            Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return $"{signingInput}.{Base64Url(signature)}";
    }

    // Base64url without padding, per JWS.
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
