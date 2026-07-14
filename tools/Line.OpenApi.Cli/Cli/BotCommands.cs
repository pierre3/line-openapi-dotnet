using Cocona;
using Line.OpenApi.Cli.Output;
using Line.OpenApi.Cli.Services;

namespace Line.OpenApi.Cli.Cli;

/// <summary>
/// <c>line bot ...</c> — B. Bot lookup (read-only): info, quota, profile.
/// </summary>
internal sealed class BotCommands
{
    private readonly CliRuntime _runtime;
    private readonly MessageService _messages;

    public BotCommands(CliRuntime runtime, MessageService messages)
    {
        _runtime = runtime;
        _messages = messages;
    }

    [Command("info", Description = "Show bot information.")]
    public Task<int> Info(GlobalOptions g)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var info = await _messages.GetBotInfoAsync(_runtime.Resolve(g), CancellationToken.None);
            if (g.Json)
            {
                Json.Print(info);
                return;
            }

            Console.WriteLine($"userId     : {info.UserId ?? "n/a"}");
            Console.WriteLine($"basicId    : {info.BasicId ?? "n/a"}");
            Console.WriteLine($"displayName: {info.DisplayName ?? "n/a"}");
            Console.WriteLine($"premiumId  : {info.PremiumId ?? "n/a"}");
            Console.WriteLine($"chatMode   : {info.ChatMode ?? "n/a"}");
        });
    }

    [Command("quota", Description = "Show the message quota (add 'consumption' for usage).")]
    public Task<int> Quota(GlobalOptions g,
        [Argument(Description = "Optional 'consumption' to show current usage instead of the limit.")] string? kind = null)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var credentials = _runtime.Resolve(g);
            if (string.Equals(kind, "consumption", StringComparison.OrdinalIgnoreCase))
            {
                var used = await _messages.GetQuotaConsumptionAsync(credentials, CancellationToken.None);
                if (g.Json)
                {
                    Json.Print(new { totalUsage = used });
                }
                else
                {
                    Console.WriteLine($"totalUsage: {used?.ToString() ?? "n/a"}");
                }

                return;
            }

            var quota = await _messages.GetQuotaAsync(credentials, CancellationToken.None);
            if (g.Json)
            {
                Json.Print(quota);
            }
            else
            {
                Console.WriteLine($"type : {quota.Type ?? "n/a"}");
                Console.WriteLine($"value: {quota.Value?.ToString() ?? "n/a"}");
            }
        });
    }

    [Command("profile", Description = "Get a user's profile.")]
    public Task<int> Profile(GlobalOptions g, [Argument(Description = "User id.")] string userId)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var profile = await _messages.GetProfileAsync(_runtime.Resolve(g), userId, CancellationToken.None);
            if (g.Json)
            {
                Json.Print(profile);
                return;
            }

            Console.WriteLine($"userId       : {profile.UserId ?? "n/a"}");
            Console.WriteLine($"displayName  : {profile.DisplayName ?? "n/a"}");
            Console.WriteLine($"language     : {profile.Language ?? "n/a"}");
            Console.WriteLine($"statusMessage: {profile.StatusMessage ?? "n/a"}");
        });
    }
}
