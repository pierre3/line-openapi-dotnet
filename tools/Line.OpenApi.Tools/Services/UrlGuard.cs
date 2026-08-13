namespace Line.OpenApi.Tools.Services;

/// <summary>
/// Validation for user-supplied endpoint URLs shared across services (webhook endpoint,
/// LIFF <c>view.url</c>). Kept neutral so services need not reach into one another.
/// </summary>
internal static class UrlGuard
{
    /// <summary>
    /// Ensures a URL is absolute and uses https (LINE's requirement for webhook and LIFF
    /// endpoints). Rejecting before any network call also gives the tools a deterministic,
    /// HTTP-free test seam. Throws <see cref="MessageInputException"/> (maps to exit code 2).
    /// </summary>
    public static void RequireHttps(string url, string paramName)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new MessageInputException($"'{paramName}' must be an absolute https URL, got '{url}'.");
        }
    }
}
