using System;

namespace Line.Messaging.Webhook;

/// <summary>
/// Webhook 受信処理（署名検証・逆直列化）で発生する例外の基底。
/// 呼び出し側は個別の派生型で分岐するか、本基底で一括捕捉できる。
/// </summary>
public class WebhookException : Exception
{
    public WebhookException(string message) : base(message) { }
    public WebhookException(string message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// <c>x-line-signature</c> の HMAC-SHA256 署名検証に失敗した（改竄・鍵不一致・ヘッダ欠落など）。
/// 受信を拒否すべき状態。ASP.NET では 401/400 にマッピングするとよい。
/// </summary>
public sealed class WebhookSignatureException : WebhookException
{
    public WebhookSignatureException(string message) : base(message) { }
}

/// <summary>
/// 署名は正当だが、本文 JSON を <see cref="Generated.Models.CallbackRequest"/> へ逆直列化できなかった。
/// </summary>
public sealed class WebhookPayloadException : WebhookException
{
    public WebhookPayloadException(string message) : base(message) { }
    public WebhookPayloadException(string message, Exception? innerException) : base(message, innerException) { }
}
