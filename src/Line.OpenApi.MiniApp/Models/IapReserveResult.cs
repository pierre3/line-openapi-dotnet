using System;
using System.Collections.Generic;
using Microsoft.Kiota.Abstractions.Serialization;

namespace Line.OpenApi.MiniApp.Models;

/// <summary>Response of <c>POST /iap/v1/product/reserve</c>.</summary>
public sealed class IapReserveResult : IParsable
{
    /// <summary>Identifier of the reserved order. Pass it on to the in-app purchase SDK.</summary>
    public string? OrderId { get; set; }

    /// <summary>Creates a new instance for Kiota deserialization.</summary>
    public static IapReserveResult CreateFromDiscriminatorValue(IParseNode parseNode)
    {
        if (parseNode is null) throw new ArgumentNullException(nameof(parseNode));
        return new IapReserveResult();
    }

    /// <inheritdoc/>
    public IDictionary<string, Action<IParseNode>> GetFieldDeserializers()
        => new Dictionary<string, Action<IParseNode>>
        {
            { "orderId", n => OrderId = n.GetStringValue() },
        };

    /// <inheritdoc/>
    public void Serialize(ISerializationWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        writer.WriteStringValue("orderId", OrderId);
    }
}
