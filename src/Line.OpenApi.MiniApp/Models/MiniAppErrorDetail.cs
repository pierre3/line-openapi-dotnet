using System;
using System.Collections.Generic;
using Microsoft.Kiota.Abstractions.Serialization;

namespace Line.OpenApi.MiniApp.Models;

/// <summary>
/// One entry of the <c>details</c> array shared by <see cref="NotifierErrorResponse"/> and
/// <see cref="IapErrorResponse"/> (same shape as the standard Messaging API error detail).
/// </summary>
public sealed class MiniAppErrorDetail : IParsable
{
    /// <summary>Details of the error. Not included in the response under certain situations.</summary>
    public string? Message { get; set; }

    /// <summary>Location of where the error occurred (JSON field name or query parameter name).</summary>
    public string? Property { get; set; }

    /// <summary>Creates a new instance for Kiota deserialization.</summary>
    public static MiniAppErrorDetail CreateFromDiscriminatorValue(IParseNode parseNode)
    {
        if (parseNode is null) throw new ArgumentNullException(nameof(parseNode));
        return new MiniAppErrorDetail();
    }

    /// <inheritdoc/>
    public IDictionary<string, Action<IParseNode>> GetFieldDeserializers()
        => new Dictionary<string, Action<IParseNode>>
        {
            { "message", n => Message = n.GetStringValue() },
            { "property", n => Property = n.GetStringValue() },
        };

    /// <inheritdoc/>
    public void Serialize(ISerializationWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        writer.WriteStringValue("message", Message);
        writer.WriteStringValue("property", Property);
    }
}
