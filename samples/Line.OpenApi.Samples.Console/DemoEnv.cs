using System;

namespace Line.OpenApi.Samples.Console;

/// <summary>
/// Reads the demo configuration from environment variables. Every value is optional: when a
/// value is absent the corresponding scenario runs "offline" (it shows how a request is built
/// but never touches the network). Supply the variables to opt in to real LINE API calls.
/// </summary>
internal static class DemoEnv
{
    /// <summary>Long-lived channel access token used by the Messaging / LIFF live paths.</summary>
    public static string? ChannelAccessToken => Get("LINE_CHANNEL_ACCESS_TOKEN");

    /// <summary>Destination user id for the "push message" scenario.</summary>
    public static string? ToUserId => Get("LINE_TO_USER_ID");

    /// <summary>Channel id (used as issuer/subject when issuing a token via JWT assertion).</summary>
    public static string? ChannelId => Get("LINE_CHANNEL_ID");

    /// <summary>Key id (JWK "kid") registered for the channel's assertion signing key.</summary>
    public static string? Kid => Get("LINE_KID");

    /// <summary>RSA private key in PEM form used to sign the JWT assertion.</summary>
    public static string? PrivateKeyPem
    {
        get
        {
            var inline = Get("LINE_PRIVATE_KEY");
            if (!string.IsNullOrEmpty(inline)) return inline;

            var path = Get("LINE_PRIVATE_KEY_PATH");
            return string.IsNullOrEmpty(path) ? null : System.IO.File.ReadAllText(path);
        }
    }

    /// <summary>True when a long-lived token is configured (Messaging / LIFF live paths available).</summary>
    public static bool HasToken => !string.IsNullOrEmpty(ChannelAccessToken);

    /// <summary>True when everything needed to sign a JWT assertion is configured.</summary>
    public static bool HasSigningKey =>
        !string.IsNullOrEmpty(ChannelId) &&
        !string.IsNullOrEmpty(Kid) &&
        !string.IsNullOrEmpty(PrivateKeyPem);

    // Treats whitespace-only values as absent, matching the library's own option validation.
    private static string? Get(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
