using System;
using System.Collections.Generic;
using Microsoft.Kiota.Abstractions.Serialization;

namespace Line.OpenApi.MiniApp.Models;

/// <summary>
/// Response of both <c>POST /message/v3/notifier/token</c> (issue) and
/// <c>POST /message/v3/notifier/send</c> (send; the token is renewed on every send and must be
/// saved for the next call). See
/// https://developers.line.biz/en/docs/line-mini-app/develop/service-messages/.
/// </summary>
public sealed class NotifierToken : IParsable
{
    /// <summary>The service notification token. Valid for 1 year; renewed on every send.</summary>
    public string? NotificationToken { get; set; }

    /// <summary>Number of seconds until the token expires.</summary>
    public long? ExpiresIn { get; set; }

    /// <summary>Number of times the token can still be used to send a service message (max 5 per user action).</summary>
    public int? RemainingCount { get; set; }

    /// <summary>Identifier of the user action the token was issued for.</summary>
    public string? SessionId { get; set; }

    /// <summary>Creates a new instance for Kiota deserialization.</summary>
    public static NotifierToken CreateFromDiscriminatorValue(IParseNode parseNode)
    {
        if (parseNode is null) throw new ArgumentNullException(nameof(parseNode));
        return new NotifierToken();
    }

    /// <inheritdoc/>
    public IDictionary<string, Action<IParseNode>> GetFieldDeserializers()
        => new Dictionary<string, Action<IParseNode>>
        {
            { "notificationToken", n => NotificationToken = n.GetStringValue() },
            { "expiresIn", n => ExpiresIn = n.GetLongValue() },
            { "remainingCount", n => RemainingCount = n.GetIntValue() },
            { "sessionId", n => SessionId = n.GetStringValue() },
        };

    /// <inheritdoc/>
    public void Serialize(ISerializationWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        writer.WriteStringValue("notificationToken", NotificationToken);
        writer.WriteLongValue("expiresIn", ExpiresIn);
        writer.WriteIntValue("remainingCount", RemainingCount);
        writer.WriteStringValue("sessionId", SessionId);
    }
}
