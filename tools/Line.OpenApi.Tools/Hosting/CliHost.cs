using Cocona;
using Line.OpenApi.Tools.Cli;
using Microsoft.Extensions.DependencyInjection;

namespace Line.OpenApi.Tools.Hosting;

/// <summary>
/// Builds and runs the Cocona-based CLI. Command implementations live under
/// <c>Cli/</c> and delegate to the shared service layer (<c>Services/</c>).
/// </summary>
internal static class CliHost
{
    public static Task<int> RunAsync(string[] args)
    {
        var builder = CoconaApp.CreateBuilder(args);
        builder.Services.AddCliCoreServices();

        var app = builder.Build();

        // Command groups (spec §4).
        app.AddCommands<DiagnosticsCommands>();
        app.AddSubCommand("config", x => x.AddCommands<ConfigCommands>())
            .WithDescription("Manage credential profiles.");
        app.AddSubCommand("token", x => x.AddCommands<TokenCommands>())
            .WithDescription("Manage channel access tokens.");
        app.AddSubCommand("message", x => x.AddCommands<MessageCommands>())
            .WithDescription("Send messages.");
        app.AddSubCommand("bot", x => x.AddCommands<BotCommands>())
            .WithDescription("Bot lookup (info / quota / profile).");
        app.AddSubCommand("webhook", x => x.AddCommands<WebhookCommands>())
            .WithDescription("Webhook development helpers.");
        app.AddSubCommand("liff", x => x.AddCommands<LiffCommands>())
            .WithDescription("Manage LIFF apps.");
        app.AddSubCommand("richmenu", x => x.AddCommands<RichMenuCommands>())
            .WithDescription("Manage rich menus (incl. image upload/download).");
        app.AddSubCommand("insight", x => x.AddCommands<InsightCommands>())
            .WithDescription("Statistics (friend demographics, deliveries, followers, events, rich menu).");
        app.AddSubCommand("audience", x => x.AddCommands<AudienceCommands>())
            .WithDescription("Manage audience groups (incl. by-file user-id upload).");
        app.AddSubCommand("shop", x => x.AddCommands<ShopCommands>())
            .WithDescription("Shop operations (mission sticker send).");

        app.Run();
        return Task.FromResult(Environment.ExitCode);
    }
}
