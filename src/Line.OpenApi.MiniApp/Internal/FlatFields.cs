using System;
using System.Collections.Generic;
using Microsoft.Kiota.Abstractions.Serialization;

namespace Line.OpenApi.MiniApp.Internal;

/// <summary>
/// A minimal <see cref="IParsable"/> request body that serializes a flat set of string fields
/// as a JSON object. Used to build the small hand-written request bodies of the LINE MINI App
/// endpoints (notifier token issue, IAP reserve) without hand-writing a class per endpoint, and
/// to serialize the dynamic <c>params</c> object of <c>notifier/send</c> as a nested value.
///
/// Null values are skipped, so a single instance can carry only the fields relevant to a
/// particular call. Serialization only; it is never deserialized.
/// </summary>
internal sealed class FlatFields : IParsable
{
    private readonly IEnumerable<KeyValuePair<string, string?>> _fields;

    public FlatFields(IEnumerable<KeyValuePair<string, string?>> fields) => _fields = fields;

    public FlatFields(params KeyValuePair<string, string?>[] fields)
        : this((IEnumerable<KeyValuePair<string, string?>>)fields)
    {
    }

    public static KeyValuePair<string, string?> Field(string name, string? value) => new(name, value);

    public IDictionary<string, Action<IParseNode>> GetFieldDeserializers()
        => new Dictionary<string, Action<IParseNode>>();

    public void Serialize(ISerializationWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        foreach (var field in _fields)
        {
            if (field.Value is not null)
                writer.WriteStringValue(field.Key, field.Value);
        }
    }
}
