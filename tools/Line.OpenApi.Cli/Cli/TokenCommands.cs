using Cocona;
using Line.OpenApi.Cli.Configuration;
using Line.OpenApi.Cli.Output;
using Line.OpenApi.Cli.Services;

namespace Line.OpenApi.Cli.Cli;

/// <summary>
/// <c>line token ...</c> — A. Token management. On the CLI the issued token is printed to
/// stdout (the user's own terminal); the MCP adapter applies non-exposure (spec §4.5).
/// </summary>
internal sealed class TokenCommands
{
    private readonly CliRuntime _runtime;
    private readonly TokenService _tokens;
    private readonly ConfigStore _config;

    public TokenCommands(CliRuntime runtime, TokenService tokens, ConfigStore config)
    {
        _runtime = runtime;
        _tokens = tokens;
        _config = config;
    }

    [Command("issue", Description = "Issue a channel access token via a signed JWT assertion.")]
    public Task<int> Issue(
        GlobalOptions g,
        [Option("kind", Description = "Token kind: v2.1 or stateless.")] string kind = "v2.1",
        [Option("days", Description = "Requested token lifetime in days (v2.1, max 30).")] int days = 30,
        [Option("channel-id", Description = "Channel id override.")] string? channelId = null,
        [Option("kid", Description = "Assertion signing key id override.")] string? kid = null,
        [Option("private-key", Description = "Path to the RSA private key (PEM) override.")] string? privateKey = null,
        [Option("store", Description = "Also store the issued token into the resolved profile.")] bool store = false)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var credentials = _runtime.Resolve(g, new CredentialOverrides
            {
                ChannelId = channelId,
                Kid = kid,
                PrivateKeyPath = privateKey,
            });

            var tokenKind = ParseKind(kind);
            var result = await _tokens.IssueAsync(credentials, tokenKind, TimeSpan.FromDays(days), CancellationToken.None);

            if (store)
            {
                _config.StoreAccessToken(credentials.ProfileName, result.AccessToken);
            }

            if (g.Json)
            {
                Json.Print(new
                {
                    accessToken = result.AccessToken,
                    tokenType = result.Kind.ToString(),
                    expiresInSeconds = result.Lifetime is { } l ? (long?)l.TotalSeconds : null,
                    keyId = result.KeyId,
                    storedProfile = store ? credentials.ProfileName : null,
                });
                return;
            }

            // Token to stdout so it can be piped; metadata to stderr so it does not pollute the value.
            Console.WriteLine(result.AccessToken);
            Console.Error.WriteLine($"type={result.Kind} expiresIn={(result.Lifetime is { } lt ? $"{lt.TotalSeconds:0}s" : "n/a")} kid={result.KeyId ?? "n/a"}");
            if (store)
            {
                Console.Error.WriteLine($"stored into profile '{credentials.ProfileName}'.");
            }
        });
    }

    [Command("verify", Description = "Verify a token's validity and remaining lifetime.")]
    public Task<int> Verify(GlobalOptions g, [Option("token", Description = "Token to verify.")] string token)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var result = await _tokens.VerifyAsync(token, CancellationToken.None);
            if (g.Json)
            {
                Json.Print(result);
                return;
            }

            if (result.Valid)
            {
                Console.WriteLine($"valid    : yes");
                Console.WriteLine($"expiresIn: {result.ExpiresInSeconds?.ToString() ?? "n/a"}s");
                Console.WriteLine($"clientId : {result.ClientId ?? "n/a"}");
            }
            else
            {
                Console.WriteLine("valid    : no");
            }
        });
    }

    [Command("revoke", Description = "Revoke a token (requires channel id and secret).")]
    public Task<int> Revoke(
        GlobalOptions g,
        [Option("token", Description = "Token to revoke.")] string token,
        [Option("channel-id", Description = "Channel id override.")] string? channelId = null,
        [Option("secret", Description = "Channel secret override.")] string? secret = null)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var credentials = _runtime.Resolve(g, new CredentialOverrides
            {
                ChannelId = channelId,
                ChannelSecret = secret,
            });
            await _tokens.RevokeAsync(credentials, token, CancellationToken.None);
            Console.WriteLine("revoked.");
        });
    }

    private static TokenKind ParseKind(string kind) => kind.Trim().ToLowerInvariant() switch
    {
        "stateless" => TokenKind.Stateless,
        "v2.1" or "v21" or "" => TokenKind.V21,
        _ => throw new MessageInputException($"Unknown token kind '{kind}'. Use 'v2.1' or 'stateless'."),
    };
}
