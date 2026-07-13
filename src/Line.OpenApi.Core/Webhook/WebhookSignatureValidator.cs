using System;
using System.Security.Cryptography;
using System.Text;

namespace Line.OpenApi.Core.Webhook;

/// <summary>
/// LINE Webhook signature validation (x-line-signature). This is not part of the OpenAPI
/// spec, so it is implemented by hand. Using the channel secret as the key, it computes the
/// HMAC-SHA256 of the request body (raw bytes), Base64-encodes it, and compares it against
/// the header value in constant time.
/// </summary>
public static class WebhookSignatureValidator
{
    public static bool IsValid(string channelSecret, byte[] requestBody, string? xLineSignatureHeader)
    {
        if (string.IsNullOrEmpty(channelSecret)) throw new ArgumentException("channel secret is required", nameof(channelSecret));
        if (requestBody is null) throw new ArgumentNullException(nameof(requestBody));
        if (string.IsNullOrEmpty(xLineSignatureHeader)) return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(channelSecret));
        var computed = hmac.ComputeHash(requestBody);
        byte[] provided;
        try { provided = Convert.FromBase64String(xLineSignatureHeader); }
        catch (FormatException) { return false; }

        // Constant-time comparison (timing-attack mitigation). Single net10.0 target, so we
        // use the standard API directly.
        return CryptographicOperations.FixedTimeEquals(computed, provided);
    }
}
