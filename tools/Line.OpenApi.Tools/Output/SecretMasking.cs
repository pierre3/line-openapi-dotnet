namespace Line.OpenApi.Tools.Output;

/// <summary>
/// Masks secret values for display. Only the last few characters are shown so a
/// human can recognize a token without the full secret ending up on screen, in
/// logs, or (for MCP) in model context.
/// </summary>
internal static class SecretMasking
{
    private const int VisibleSuffix = 4;

    /// <summary>Returns a masked form such as <c>…AbCd</c>, or <c>&lt;unset&gt;</c> when null/blank.</summary>
    public static string Mask(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "<unset>";
        }

        if (value.Length <= VisibleSuffix)
        {
            return new string('•', value.Length);
        }

        return "…" + value[^VisibleSuffix..];
    }
}
