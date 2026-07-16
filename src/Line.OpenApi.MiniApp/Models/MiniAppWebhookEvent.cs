using System;
using System.Collections.Generic;
using Microsoft.Kiota.Abstractions.Serialization;

namespace Line.OpenApi.MiniApp.Models;

/// <summary>
/// IAP webhook event payload (nested under <see cref="MiniAppWebhookEventEntry.Event"/>).
/// <see cref="Type"/> is <c>purchaseComplete</c> or <c>refundComplete</c>; both share this exact
/// field shape, so no polymorphic subtype is needed.
/// </summary>
public sealed class MiniAppWebhookEvent : IParsable
{
    /// <summary>Event type: <c>purchaseComplete</c> or <c>refundComplete</c>.</summary>
    public string? Type { get; set; }

    /// <summary>Identifier of the (possibly refunded) order.</summary>
    public string? OrderId { get; set; }

    /// <summary>Identifier of the purchased product.</summary>
    public string? ProductId { get; set; }

    /// <summary>Identifier of the purchasing user.</summary>
    public string? UserId { get; set; }

    /// <summary>UNIX time (seconds) of the original purchase.</summary>
    public long? PurchaseTimestamp { get; set; }

    /// <summary>Channel ID of the MINI App.</summary>
    public string? ChannelId { get; set; }

    /// <summary>Creates a new instance for Kiota deserialization.</summary>
    public static MiniAppWebhookEvent CreateFromDiscriminatorValue(IParseNode parseNode)
    {
        if (parseNode is null) throw new ArgumentNullException(nameof(parseNode));
        return new MiniAppWebhookEvent();
    }

    /// <inheritdoc/>
    public IDictionary<string, Action<IParseNode>> GetFieldDeserializers()
        => new Dictionary<string, Action<IParseNode>>
        {
            { "type", n => Type = n.GetStringValue() },
            { "orderId", n => OrderId = n.GetStringValue() },
            { "productId", n => ProductId = n.GetStringValue() },
            { "userId", n => UserId = n.GetStringValue() },
            { "purchaseTimestamp", n => PurchaseTimestamp = n.GetLongValue() },
            { "channelId", n => ChannelId = n.GetStringValue() },
        };

    /// <inheritdoc/>
    public void Serialize(ISerializationWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        writer.WriteStringValue("type", Type);
        writer.WriteStringValue("orderId", OrderId);
        writer.WriteStringValue("productId", ProductId);
        writer.WriteStringValue("userId", UserId);
        writer.WriteLongValue("purchaseTimestamp", PurchaseTimestamp);
        writer.WriteStringValue("channelId", ChannelId);
    }
}
