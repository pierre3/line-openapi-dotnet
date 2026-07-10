using System;
using System.Security.Cryptography;
using System.Text;

namespace Line.Core.Webhook;

/// <summary>
/// LINE Webhook の署名検証（x-line-signature）。仕様(OpenAPI)には含まれないため手書き実装。
/// チャネルシークレットを鍵に、リクエストボディ(生バイト)の HMAC-SHA256 を Base64 化し、
/// ヘッダ値と定数時間比較する。
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

        // 定数時間比較（タイミング攻撃対策）。net10.0 単一ターゲットのため標準 API を直接使用。
        return CryptographicOperations.FixedTimeEquals(computed, provided);
    }
}
