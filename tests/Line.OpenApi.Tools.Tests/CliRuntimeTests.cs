using Line.OpenApi.Tools;
using Line.OpenApi.Tools.Cli;
using Line.OpenApi.Tools.Configuration;
using Line.OpenApi.Tools.Services;
using Line.OpenApi.Messaging.Webhook;
using Microsoft.Kiota.Abstractions;
using Xunit;

namespace Line.OpenApi.Tools.Tests;

public sealed class CliRuntimeTests
{
    private static readonly CliRuntime Runtime =
        new(new CredentialResolver(new ConfigStore(Path.Combine(Path.GetTempPath(), $"cli-rt-{Guid.NewGuid():N}.json"))));

    private static readonly GlobalOptions Options = new();

    private static async Task<int> Run(Exception toThrow) =>
        await Runtime.ExecuteAsync(Options, () => throw toThrow);

    [Fact]
    public async Task Success_returns_zero() =>
        Assert.Equal(ExitCodes.Success, await Runtime.ExecuteAsync(Options, () => Task.CompletedTask));

    [Fact]
    public async Task Credential_error_maps_to_3() =>
        Assert.Equal(ExitCodes.CredentialError, await Run(new CredentialException("no token")));

    [Fact]
    public async Task Argument_errors_map_to_2()
    {
        Assert.Equal(ExitCodes.ArgumentError, await Run(new MessageInputException("bad input")));
        Assert.Equal(ExitCodes.ArgumentError, await Run(new ConfigException("bad config")));
        Assert.Equal(ExitCodes.ArgumentError, await Run(new FileNotFoundException("missing")));
        Assert.Equal(ExitCodes.ArgumentError, await Run(new DirectoryNotFoundException("missing dir")));
    }

    [Fact]
    public async Task Webhook_errors_map_to_1()
    {
        Assert.Equal(ExitCodes.GeneralError, await Run(new WebhookSignatureException("bad sig")));
        Assert.Equal(ExitCodes.GeneralError, await Run(new WebhookPayloadException("bad body")));
    }

    [Fact]
    public async Task Api_error_maps_to_4() =>
        Assert.Equal(ExitCodes.ApiError, await Run(new ApiException("boom") { ResponseStatusCode = 500 }));

    [Fact]
    public async Task Generic_error_maps_to_1() =>
        Assert.Equal(ExitCodes.GeneralError, await Run(new InvalidOperationException("unexpected")));
}
