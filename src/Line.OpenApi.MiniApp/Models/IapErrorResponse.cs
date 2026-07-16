using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;

namespace Line.OpenApi.MiniApp.Models;

/// <summary>
/// Error body returned by the in-app purchase endpoints (<c>/iap/v1/*</c>) on a non-2xx
/// response, for example <c>{"errorCode":"PRODUCT_ID_NOT_FOUND","message":"..."}</c>. It derives
/// from <see cref="ApiException"/> so it is thrown by the client with the HTTP status code
/// preserved, while also exposing <see cref="ErrorCode"/> (for example
/// <c>VALIDATION_ERROR</c>, <c>PRODUCT_ID_NOT_FOUND</c>, <c>BLOCKED_USER</c>,
/// <c>TERMS_AGREEMENT_ERROR</c>).
/// </summary>
public sealed class IapErrorResponse : ApiException, IParsable
{
    /// <summary>The primary error message.</summary>
    public override string Message =>
        ErrorCode is null
            ? MessageText ?? base.Message
            : MessageText is null ? ErrorCode : $"{ErrorCode}: {MessageText}";

    /// <summary>Machine-readable error code (for example <c>PRODUCT_ID_NOT_FOUND</c>).</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Human-readable message describing the error.</summary>
    public string? MessageText { get; set; }

    /// <summary>An array of error details. Not included in the response under certain situations.</summary>
    public List<MiniAppErrorDetail>? Details { get; set; }

    /// <summary>Creates a new instance for Kiota error deserialization.</summary>
    public static IapErrorResponse CreateFromDiscriminatorValue(IParseNode parseNode)
    {
        if (parseNode is null) throw new ArgumentNullException(nameof(parseNode));
        return new IapErrorResponse();
    }

    /// <inheritdoc/>
    public IDictionary<string, Action<IParseNode>> GetFieldDeserializers()
        => new Dictionary<string, Action<IParseNode>>
        {
            { "errorCode", n => ErrorCode = n.GetStringValue() },
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
        writer.WriteStringValue("errorCode", ErrorCode);
        writer.WriteStringValue("message", MessageText);
        writer.WriteCollectionOfObjectValues("details", Details);
    }
}
