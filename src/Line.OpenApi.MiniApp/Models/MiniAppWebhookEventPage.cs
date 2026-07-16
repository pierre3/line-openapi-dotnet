using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Kiota.Abstractions.Serialization;

namespace Line.OpenApi.MiniApp.Models;

/// <summary>
/// Response of <c>GET /iap/v1/webhook/events</c>: one page of the past 7 days of IAP webhook
/// events, cursor-paginated.
/// </summary>
public sealed class MiniAppWebhookEventPage : IParsable
{
    /// <summary>The events on this page.</summary>
    public List<MiniAppWebhookEventEntry>? Events { get; set; }

    /// <summary>Cursor for the next page, or <c>null</c> when there is no more data.</summary>
    public string? NextCursor { get; set; }

    /// <summary>Creates a new instance for Kiota deserialization.</summary>
    public static MiniAppWebhookEventPage CreateFromDiscriminatorValue(IParseNode parseNode)
    {
        if (parseNode is null) throw new ArgumentNullException(nameof(parseNode));
        return new MiniAppWebhookEventPage();
    }

    /// <inheritdoc/>
    public IDictionary<string, Action<IParseNode>> GetFieldDeserializers()
        => new Dictionary<string, Action<IParseNode>>
        {
            {
                "events",
                n => Events = n.GetCollectionOfObjectValues(MiniAppWebhookEventEntry.CreateFromDiscriminatorValue)
                    ?.ToList()
            },
            { "nextCursor", n => NextCursor = n.GetStringValue() },
        };

    /// <inheritdoc/>
    public void Serialize(ISerializationWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        writer.WriteCollectionOfObjectValues("events", Events);
        writer.WriteStringValue("nextCursor", NextCursor);
    }
}
