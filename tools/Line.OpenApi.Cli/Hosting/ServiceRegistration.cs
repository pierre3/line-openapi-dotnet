using Line.OpenApi.Cli.Cli;
using Line.OpenApi.Cli.Configuration;
using Line.OpenApi.Cli.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Line.OpenApi.Cli.Hosting;

/// <summary>
/// Registers services shared by both execution modes (CLI and MCP). The shared
/// service layer is the single implementation of each operation; the Cocona and
/// MCP adapters are thin wrappers over it (spec §2).
/// </summary>
internal static class ServiceRegistration
{
    public static IServiceCollection AddCliCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<ConfigStore>();
        services.AddSingleton<CredentialResolver>();

        // Shared service layer (spec §2): the single implementation of each operation.
        services.AddSingleton<TokenService>();
        services.AddSingleton<MessageService>();
        services.AddSingleton<WebhookService>();
        services.AddSingleton<LiffService>();

        // CLI adapter helper (credential resolution + exit-code mapping).
        services.AddSingleton<CliRuntime>();

        return services;
    }
}
