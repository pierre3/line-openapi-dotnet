using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;

namespace Line.OpenApi.MiniApp.Models;

/// <summary>
/// Error body returned by the service-message endpoints (<c>/message/v3/notifier/*</c>) on a
/// non-2xx response. It derives from <see cref="ApiException"/> so it is thrown by the client
/// with the HTTP status code preserved, while also exposing <see cref="Message"/> and
/// <see cref="Details"/> (same shape as the standard Messaging API error).
/// </summary>
public sealed class NotifierErrorResponse : ApiException, IParsable
{
    /// <summary>The primary error message.</summary>
    public override string Message => MessageText ?? base.Message;

    /// <summary>Message containing information about the error.</summary>
    public string? MessageText { get; set; }

    /// <summary>An array of error details. Not included in the response under certain situations.</summary>
    public List<MiniAppErrorDetail>? Details { get; set; }

    /// <summary>Creates a new instance for Kiota error deserialization.</summary>
    public static NotifierErrorResponse CreateFromDiscriminatorValue(IParseNode parseNode)
    {
        if (parseNode is null) throw new ArgumentNullException(nameof(parseNode));
        return new NotifierErrorResponse();
    }

    /// <inheritdoc/>
    public IDictionary<string, Action<IParseNode>> GetFieldDeserializers()
        => new Dictionary<string, Action<IParseNode>>
        {
            { "message", n => MessageText = n.GetStringValue() },
            {
                "details",
                n => Details = n.GetCollectionOfObjectValues(MiniAppErrorDetail.CreateFromDiscriminatorValue)
                    ?.ToList()
            },
        };

    /// <inheritdoc/>
    public void Serialize(ISerializationWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        writer.WriteStringValue("message", MessageText);
        writer.WriteCollectionOfObjectValues("details", Details);
    }
}
