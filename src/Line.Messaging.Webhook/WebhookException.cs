using System;

namespace Line.Messaging.Webhook;

/// <summary>
/// Base type for exceptions raised during webhook receive processing (signature validation /
/// deserialization). Callers can branch on the individual derived types, or catch them all
/// through this base.
/// </summary>
public class WebhookException : Exception
{
    public WebhookException(string message) : base(message) { }
    public WebhookException(string message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// The HMAC-SHA256 signature validation of <c>x-line-signature</c> failed (tampering, key
/// mismatch, missing header, etc.). The request should be rejected. In ASP.NET, mapping this
/// to 401/400 is a good choice.
/// </summary>
public sealed class WebhookSignatureException : WebhookException
{
    public WebhookSignatureException(string message) : base(message) { }
}

/// <summary>
/// The signature was valid, but the body JSON could not be deserialized into
/// <see cref="Generated.Models.CallbackRequest"/>.
/// </summary>
public sealed class WebhookPayloadException : WebhookException
{
    public WebhookPayloadException(string message) : base(message) { }
    public WebhookPayloadException(string message, Exception? innerException) : base(message, innerException) { }
}
