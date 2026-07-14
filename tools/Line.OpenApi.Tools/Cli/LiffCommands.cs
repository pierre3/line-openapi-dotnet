using Cocona;
using Line.OpenApi.Tools.Output;
using Line.OpenApi.Tools.Services;

namespace Line.OpenApi.Tools.Cli;

/// <summary>
/// <c>line liff ...</c> — D. LIFF app management.
/// </summary>
internal sealed class LiffCommands
{
    private readonly CliRuntime _runtime;
    private readonly LiffService _liff;

    public LiffCommands(CliRuntime runtime, LiffService liff)
    {
        _runtime = runtime;
        _liff = liff;
    }

    [Command("list", Description = "List registered LIFF apps.")]
    public Task<int> List(GlobalOptions g)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var apps = await _liff.ListAsync(_runtime.Resolve(g), CancellationToken.None);
            if (g.Json)
            {
                Json.Print(apps);
                return;
            }

            if (apps.Count == 0)
            {
                Console.WriteLine("(no LIFF apps)");
                return;
            }

            foreach (var app in apps)
            {
                Console.WriteLine($"{app.LiffId}  [{app.ViewType ?? "?"}]  {app.Url}  {app.Description}");
            }
        });
    }

    [Command("add", Description = "Add a LIFF app from a JSON definition file.")]
    public Task<int> Add(GlobalOptions g, [Option("file", Description = "Path to the LIFF app definition JSON.")] string file)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var json = await File.ReadAllTextAsync(file, CancellationToken.None);
            var liffId = await _liff.AddAsync(_runtime.Resolve(g), json, CancellationToken.None);
            if (g.Json)
            {
                Json.Print(new { liffId });
            }
            else
            {
                Console.WriteLine($"added liffId = {liffId}");
            }
        });
    }

    [Command("update", Description = "Update a LIFF app from a JSON definition file.")]
    public Task<int> Update(GlobalOptions g,
        [Argument(Description = "LIFF app id.")] string liffId,
        [Option("file", Description = "Path to the LIFF app definition JSON.")] string file)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var json = await File.ReadAllTextAsync(file, CancellationToken.None);
            await _liff.UpdateAsync(_runtime.Resolve(g), liffId, json, CancellationToken.None);
            Console.WriteLine($"updated {liffId}");
        });
    }

    [Command("delete", Description = "Delete a LIFF app.")]
    public Task<int> Delete(GlobalOptions g, [Argument(Description = "LIFF app id.")] string liffId)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            await _liff.DeleteAsync(_runtime.Resolve(g), liffId, CancellationToken.None);
            Console.WriteLine($"deleted {liffId}");
        });
    }
}
