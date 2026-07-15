using System;
using System.Collections.Generic;
using Microsoft.Kiota.Abstractions.Serialization;

namespace Line.OpenApi.Login.Internal;

/// <summary>
/// A minimal <see cref="IParsable"/> request body that serializes a flat set of string fields.
/// Used to build the flat <c>application/x-www-form-urlencoded</c> bodies of the
/// <c>/oauth2/v2.1/*</c> endpoints (and the small JSON body of <c>/user/v1/deauthorize</c>)
/// without hand-writing a class per endpoint.
///
/// Null values are skipped, so a single instance can carry only the fields relevant to a
/// particular call (for example, <c>code_verifier</c> only when PKCE is used). Serialization
/// only; it is never deserialized.
/// </summary>
internal sealed class FormFields : IParsable
{
    private readonly IReadOnlyList<KeyValuePair<string, string?>> _fields;

    public FormFields(params KeyValuePair<string, string?>[] fields) => _fields = fields;

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
