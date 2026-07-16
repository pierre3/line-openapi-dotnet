using System;
using System.Collections.Generic;
using Microsoft.Kiota.Abstractions.Serialization;

namespace Line.OpenApi.MiniApp.Models;

/// <summary>One entry of <see cref="MiniAppWebhookEventPage.Events"/>.</summary>
public sealed class MiniAppWebhookEventEntry : IParsable
{
    /// <summary>Transaction type. Always <c>PRODUCT</c> for IAP events.</summary>
    public string? TransactionType { get; set; }

    /// <summary>The webhook event payload.</summary>
    public MiniAppWebhookEvent? Event { get; set; }

    /// <summary>Creates a new instance for Kiota deserialization.</summary>
    public static MiniAppWebhookEventEntry CreateFromDiscriminatorValue(IParseNode parseNode)
    {
        if (parseNode is null) throw new ArgumentNullException(nameof(parseNode));
        return new MiniAppWebhookEventEntry();
    }

    /// <inheritdoc/>
    public IDictionary<string, Action<IParseNode>> GetFieldDeserializers()
        => new Dictionary<string, Action<IParseNode>>
        {
            { "transactionType", n => TransactionType = n.GetStringValue() },
            { "event", n => Event = n.GetObjectValue(MiniAppWebhookEvent.CreateFromDiscriminatorValue) },
        };

    /// <inheritdoc/>
    public void Serialize(ISerializationWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        writer.WriteStringValue("transactionType", TransactionType);
        writer.WriteObjectValue("event", Event);
    }
}
