using System.Threading.Tasks;
using Line.OpenApi.Liff;
using Line.OpenApi.Liff.Generated.Models;

using Con = System.Console;

namespace Line.OpenApi.Samples.Console.Scenarios;

/// <summary>
/// Shows LIFF app management with <see cref="LiffClient"/>. Offline it prints the CRUD calls;
/// with a token it lists the registered apps (read-only). Full add/update/delete round-trip runs
/// only when <paramref name="crud"/> is true (mutating), because it changes channel state.
/// </summary>
internal static class LiffScenario
{
    public static async Task RunAsync(bool crud)
    {
        Con.WriteLine("== LIFF: manage apps ==\n");
        Con.WriteLine("  list   : GET    https://api.line.me/liff/v1/apps");
        Con.WriteLine("  add    : POST   https://api.line.me/liff/v1/apps");
        Con.WriteLine("  update : PUT    https://api.line.me/liff/v1/apps/{liffId}");
        Con.WriteLine("  delete : DELETE https://api.line.me/liff/v1/apps/{liffId}\n");

        if (!DemoEnv.HasToken)
        {
            Con.WriteLine("[offline] LINE_CHANNEL_ACCESS_TOKEN is not set — no calls made.");
            Con.WriteLine("          Set LINE_CHANNEL_ACCESS_TOKEN to list apps; add 'crud' to run add/update/delete.");
            return;
        }

        var liff = LiffClient.CreateWithStaticToken(DemoEnv.ChannelAccessToken!);

        // Read-only: always safe to run against a live channel.
        var apps = await liff.GetAppsAsync();
        var count = apps?.Apps?.Count ?? 0;
        Con.WriteLine($"[live] {count} LIFF app(s) registered.");
        if (apps?.Apps is { Count: > 0 })
        {
            foreach (var app in apps.Apps)
                Con.WriteLine($"       - {app.LiffId}  {app.View?.Url}");
        }

        if (!crud)
        {
            Con.WriteLine("\n(add 'crud' to also demo add/update/delete — this mutates the channel.)");
            return;
        }

        // Mutating round-trip: add, update, then delete the same app so the channel is left clean.
        Con.WriteLine("\n[live] Adding a temporary LIFF app...");
        var added = await liff.AddAppAsync(new AddLiffAppRequest
        {
            View = new LiffView { Type = LiffView_type.Full, Url = "https://example.com/demo" },
            Description = "Line.OpenApi .NET sample (temporary)",
        });

        var liffId = added?.LiffId;
        Con.WriteLine($"       added liffId = {liffId}");

        if (!string.IsNullOrEmpty(liffId))
        {
            await liff.UpdateAppAsync(liffId, new UpdateLiffAppRequest { Description = "updated by sample" });
            Con.WriteLine("       updated description");

            await liff.DeleteAppAsync(liffId);
            Con.WriteLine("       deleted (channel left clean)");
        }
    }
}
