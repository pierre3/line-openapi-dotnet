using Cocona;
using Line.OpenApi.Tools.Output;
using Line.OpenApi.Tools.Services;

namespace Line.OpenApi.Tools.Cli;

/// <summary>
/// <c>line richmenu ...</c> — Rich Menu management. Covers the full dev cycle including binary
/// image upload/download (which the MCP surface delegates here). Definitions are supplied as JSON
/// files; the image is a PNG/JPEG file.
/// </summary>
internal sealed class RichMenuCommands
{
    private readonly CliRuntime _runtime;
    private readonly RichMenuService _richMenu;

    public RichMenuCommands(CliRuntime runtime, RichMenuService richMenu)
    {
        _runtime = runtime;
        _richMenu = richMenu;
    }

    [Command("list", Description = "List the channel's rich menus.")]
    public Task<int> List(GlobalOptions g)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var menus = await _richMenu.ListAsync(_runtime.Resolve(g), CancellationToken.None);
            if (g.Json)
            {
                Json.Print(menus);
                return;
            }

            if (menus.Count == 0)
            {
                Console.WriteLine("(no rich menus)");
                return;
            }

            foreach (var m in menus)
            {
                Console.WriteLine($"{m.RichMenuId}  {(m.Selected == true ? "*" : " ")} {m.Name}  [{m.AreaCount} areas]  \"{m.ChatBarText}\"");
            }
        });
    }

    [Command("get", Description = "Get a rich menu by id.")]
    public Task<int> Get(GlobalOptions g, [Argument(Description = "Rich menu id.")] string richMenuId)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var menu = await _richMenu.GetAsync(_runtime.Resolve(g), richMenuId, CancellationToken.None);
            if (menu is null)
            {
                Console.WriteLine("(not found)");
            }
            else
            {
                Json.Print(menu);
            }
        });
    }

    [Command("create", Description = "Create a rich menu from a JSON definition file. Prints the new rich menu id.")]
    public Task<int> Create(GlobalOptions g, [Option("file", Description = "Path to the rich menu definition JSON.")] string file)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var json = await File.ReadAllTextAsync(file, CancellationToken.None);
            var id = await _richMenu.CreateAsync(_runtime.Resolve(g), json, CancellationToken.None);
            if (g.Json)
            {
                Json.Print(new { richMenuId = id });
            }
            else
            {
                Console.WriteLine($"created richMenuId = {id}");
            }
        });
    }

    [Command("validate", Description = "Validate a rich menu JSON definition without creating it.")]
    public Task<int> Validate(GlobalOptions g, [Option("file", Description = "Path to the rich menu definition JSON.")] string file)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var json = await File.ReadAllTextAsync(file, CancellationToken.None);
            var result = await _richMenu.ValidateAsync(_runtime.Resolve(g), json, CancellationToken.None);
            if (g.Json)
            {
                Json.Print(result);
            }
            else
            {
                Console.WriteLine($"valid: {result.Name} ({result.AreaCount} areas)");
            }
        });
    }

    [Command("delete", Description = "Delete a rich menu.")]
    public Task<int> Delete(GlobalOptions g, [Argument(Description = "Rich menu id.")] string richMenuId)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            await _richMenu.DeleteAsync(_runtime.Resolve(g), richMenuId, CancellationToken.None);
            Console.WriteLine($"deleted {richMenuId}");
        });
    }

    [Command("image", Description = "Upload a rich menu image (PNG/JPEG; content type inferred from the extension).")]
    public Task<int> Image(GlobalOptions g,
        [Argument(Description = "Rich menu id.")] string richMenuId,
        [Option("file", Description = "Path to the image file (.png / .jpg / .jpeg).")] string file)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            await _richMenu.SetImageFromFileAsync(_runtime.Resolve(g), richMenuId, file, CancellationToken.None);
            Console.WriteLine($"uploaded image for {richMenuId} from {file}");
        });
    }

    [Command("image-download", Description = "Download a rich menu image to a file.")]
    public Task<int> ImageDownload(GlobalOptions g,
        [Argument(Description = "Rich menu id.")] string richMenuId,
        [Option('o', Description = "Output file path.")] string output)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var bytes = await _richMenu.DownloadImageAsync(_runtime.Resolve(g), richMenuId, output, CancellationToken.None);
            Console.WriteLine($"downloaded {bytes} bytes to {output}");
        });
    }

    [Command("set-default", Description = "Set the default rich menu for all users.")]
    public Task<int> SetDefault(GlobalOptions g, [Argument(Description = "Rich menu id.")] string richMenuId)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            await _richMenu.SetDefaultAsync(_runtime.Resolve(g), richMenuId, CancellationToken.None);
            Console.WriteLine($"set default = {richMenuId}");
        });
    }

    [Command("get-default", Description = "Get the default rich menu id.")]
    public Task<int> GetDefault(GlobalOptions g)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var id = await _richMenu.GetDefaultIdAsync(_runtime.Resolve(g), CancellationToken.None);
            if (g.Json)
            {
                Json.Print(new { richMenuId = id });
            }
            else
            {
                Console.WriteLine(id ?? "(no default rich menu)");
            }
        });
    }

    [Command("cancel-default", Description = "Cancel the default rich menu.")]
    public Task<int> CancelDefault(GlobalOptions g)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            await _richMenu.CancelDefaultAsync(_runtime.Resolve(g), CancellationToken.None);
            Console.WriteLine("cancelled default rich menu");
        });
    }

    [Command("link", Description = "Link a rich menu to a user.")]
    public Task<int> Link(GlobalOptions g,
        [Argument(Description = "User id.")] string userId,
        [Argument(Description = "Rich menu id.")] string richMenuId)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            await _richMenu.LinkToUserAsync(_runtime.Resolve(g), userId, richMenuId, CancellationToken.None);
            Console.WriteLine($"linked {richMenuId} to {userId}");
        });
    }

    [Command("unlink", Description = "Unlink the rich menu from a user.")]
    public Task<int> Unlink(GlobalOptions g, [Argument(Description = "User id.")] string userId)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            await _richMenu.UnlinkFromUserAsync(_runtime.Resolve(g), userId, CancellationToken.None);
            Console.WriteLine($"unlinked rich menu from {userId}");
        });
    }

    [Command("id-of-user", Description = "Get the rich menu id linked to a user.")]
    public Task<int> IdOfUser(GlobalOptions g, [Argument(Description = "User id.")] string userId)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var id = await _richMenu.GetIdOfUserAsync(_runtime.Resolve(g), userId, CancellationToken.None);
            if (g.Json)
            {
                Json.Print(new { richMenuId = id });
            }
            else
            {
                Console.WriteLine(id ?? "(no rich menu linked)");
            }
        });
    }
}
