using System;
using System.Threading.Tasks;
using Line.OpenApi.Samples.Console;
using Line.OpenApi.Samples.Console.Scenarios;

// LINE OpenApi .NET — console samples.
//
// Offline by default: with no environment variables set, each scenario prints how a request is
// built without any network calls. Set the documented variables (see samples/README.md) to opt
// in to real LINE API calls.
//
// Usage:
//   dotnet run                 # interactive menu
//   dotnet run -- send         # run a single scenario and exit (send | liff | token | webhook)

using Con = System.Console;

var scenario = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : null;

if (scenario is not null)
{
    return await RunAsync(scenario);
}

// Interactive menu loop.
while (true)
{
    PrintBanner();
    Con.WriteLine("  1) Messaging — push a text message");
    Con.WriteLine("  2) LIFF       — list apps (+ optional CRUD with 'liff crud')");
    Con.WriteLine("  3) Token      — issue a channel access token (JWT assertion)");
    Con.WriteLine("  4) Webhook    — parse a sample payload (always offline)");
    Con.WriteLine("  0) Exit");
    Con.Write("\nSelect: ");

    var choice = Con.ReadLine()?.Trim();
    Con.WriteLine();

    var code = choice switch
    {
        "1" => await RunAsync("send"),
        "2" => await RunAsync("liff"),
        "3" => await RunAsync("token"),
        "4" => await RunAsync("webhook"),
        "0" or "" or null => -1,
        _ => Unknown(choice),
    };

    if (code == -1) break;
    Con.WriteLine("\nPress Enter to continue...");
    Con.ReadLine();
}

return 0;

static int Unknown(string choice)
{
    Con.WriteLine($"Unknown selection: '{choice}'.");
    return 0;
}

static async Task<int> RunAsync(string scenario)
{
    try
    {
        switch (scenario)
        {
            case "send":
            case "message":
            case "messaging":
                await MessagingScenario.RunAsync();
                return 0;
            case "liff":
                await LiffScenario.RunAsync(crud: HasCrudFlag());
                return 0;
            case "token":
                await TokenScenario.RunAsync();
                return 0;
            case "webhook":
                await WebhookParseScenario.RunAsync();
                return 0;
            default:
                Con.WriteLine($"Unknown scenario '{scenario}'. Use: send | liff | token | webhook.");
                return 1;
        }
    }
    catch (Exception ex)
    {
        // Demo-friendly error surface: show the type and message, no stack trace noise.
        Con.WriteLine($"\n[error] {ex.GetType().Name}: {ex.Message}");
        return 1;
    }

    // The 'crud' flag is read from the process args, so it only applies to single-shot runs
    // (e.g. `dotnet run -- liff crud`); the interactive menu always runs LIFF in read-only mode.
    static bool HasCrudFlag() =>
        Array.Exists(Environment.GetCommandLineArgs(), a =>
            a.Equals("crud", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--liff-crud", StringComparison.OrdinalIgnoreCase));
}

static void PrintBanner()
{
    Con.WriteLine();
    Con.WriteLine("==================================================");
    Con.WriteLine(" LINE OpenApi .NET — Console Samples");
    Con.WriteLine($" Mode: {(DemoEnv.HasToken ? "LIVE (token configured)" : "OFFLINE (no token)")}");
    Con.WriteLine("==================================================");
}
