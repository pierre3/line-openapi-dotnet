using Line.OpenApi.Cli.Configuration;
using Line.OpenApi.Cli.Services;
using Line.OpenApi.Messaging.Webhook;
using Microsoft.Kiota.Abstractions;

namespace Line.OpenApi.Cli.Cli;

/// <summary>
/// Shared helpers for CLI command adapters: credential resolution from global options and a
/// uniform exception → exit code mapping (spec §6).
/// </summary>
internal sealed class CliRuntime
{
    private readonly CredentialResolver _resolver;

    public CliRuntime(CredentialResolver resolver)
    {
        _resolver = resolver;
    }

    /// <summary>Resolves credentials, layering command-specific overrides over the global options.</summary>
    public ResolvedCredentials Resolve(GlobalOptions options, CredentialOverrides? extra = null) =>
        _resolver.Resolve(new CredentialOverrides
        {
            ProfileName = options.Profile,
            ChannelAccessToken = options.ChannelToken ?? extra?.ChannelAccessToken,
            ChannelId = extra?.ChannelId,
            ChannelSecret = extra?.ChannelSecret,
            PrivateKeyPath = extra?.PrivateKeyPath,
            Kid = extra?.Kid,
        });

    /// <summary>Runs a command body and maps exceptions to exit codes.</summary>
    public async Task<int> ExecuteAsync(GlobalOptions options, Func<Task> body)
    {
        try
        {
            await body().ConfigureAwait(false);
            return ExitCodes.Success;
        }
        catch (CredentialException ex)
        {
            return Fail(ex.Message, ExitCodes.CredentialError);
        }
        catch (MessageInputException ex)
        {
            return Fail(ex.Message, ExitCodes.ArgumentError);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // A missing input file (--body/--flex/--messages/--file) is an argument-level error.
            return Fail(ex.Message, ExitCodes.ArgumentError);
        }
        catch (ConfigException ex)
        {
            return Fail(ex.Message, ExitCodes.ArgumentError);
        }
        catch (WebhookException ex)
        {
            // Signature or payload failure — expected, not a crash.
            return Fail(ex.Message, ExitCodes.GeneralError);
        }
        catch (ApiException ex)
        {
            return Fail($"LINE API error (HTTP {ex.ResponseStatusCode}): {ex.Message}", ExitCodes.ApiError,
                options.Verbose ? ex.ToString() : null);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message, ExitCodes.GeneralError, options.Verbose ? ex.ToString() : null);
        }
    }

    private static int Fail(string message, int code, string? verbose = null)
    {
        Console.Error.WriteLine($"error: {SecretScrubber.Scrub(message)}");
        if (verbose is not null)
        {
            Console.Error.WriteLine(SecretScrubber.Scrub(verbose));
        }

        return code;
    }
}

/// <summary>
/// Defensively redacts secret-shaped substrings (query <c>access_token</c>, bearer tokens) from
/// error/verbose output, in case an exception message ever embeds a URL or header (security gate Low).
/// </summary>
internal static partial class SecretScrubber
{
    [System.Text.RegularExpressions.GeneratedRegex(@"(access_token=)[^&\s""]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex AccessTokenQuery();

    [System.Text.RegularExpressions.GeneratedRegex(@"(Bearer\s+)[A-Za-z0-9._\-]+", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex BearerHeader();

    public static string Scrub(string input)
    {
        var scrubbed = AccessTokenQuery().Replace(input, "$1<redacted>");
        return BearerHeader().Replace(scrubbed, "$1<redacted>");
    }
}
