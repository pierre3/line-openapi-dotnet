using Cocona;
using Line.OpenApi.Tools.Output;
using Line.OpenApi.Tools.Services;

namespace Line.OpenApi.Tools.Cli;

/// <summary>
/// <c>line audience ...</c> — manage audience groups. The by-file uploads (upload-file / add-file)
/// take a text file of user IDs (one per line) and are CLI-only (binary/file input is impractical
/// over MCP).
/// </summary>
internal sealed class AudienceCommands
{
    private readonly CliRuntime _runtime;
    private readonly AudienceService _audience;

    public AudienceCommands(CliRuntime runtime, AudienceService audience)
    {
        _runtime = runtime;
        _audience = audience;
    }

    [Command("list", Description = "List audience groups (paginated).")]
    public Task<int> List(GlobalOptions g,
        [Option("page", Description = "Page to return (1 or higher).")] long page = 1,
        [Option("size", Description = "Audiences per page (default 20, max 40).")] long size = 20)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var groups = await _audience.ListAsync(_runtime.Resolve(g), page, size, CancellationToken.None);
            if (g.Json)
            {
                Json.Print(groups);
                return;
            }

            if (groups.Count == 0)
            {
                Console.WriteLine("(no audience groups)");
                return;
            }

            foreach (var group in groups)
            {
                Console.WriteLine($"{group.AudienceGroupId}  [{group.Status ?? "?"}]  count={group.AudienceCount}  {group.Description}");
            }
        });
    }

    [Command("get", Description = "Get an audience group and its jobs.")]
    public Task<int> Get(GlobalOptions g, [Argument(Description = "Audience group id.")] long audienceGroupId) =>
        _runtime.ExecuteAsync(g, async () =>
            Json.Print(await _audience.GetAsync(_runtime.Resolve(g), audienceGroupId, CancellationToken.None)));

    [Command("create", Description = "Create an audience group from a JSON request file (with initial user IDs).")]
    public Task<int> Create(GlobalOptions g, [Option("file", Description = "Path to the CreateAudienceGroupRequest JSON.")] string file)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var json = await File.ReadAllTextAsync(file, CancellationToken.None);
            var audienceGroupId = await _audience.CreateAsync(_runtime.Resolve(g), json, CancellationToken.None);
            if (g.Json)
            {
                Json.Print(new { audienceGroupId });
            }
            else
            {
                Console.WriteLine($"created audienceGroupId = {audienceGroupId}");
            }
        });
    }

    [Command("add-users", Description = "Add user IDs to an existing group from a JSON request file (carries audienceGroupId).")]
    public Task<int> AddUsers(GlobalOptions g, [Option("file", Description = "Path to the AddAudienceToAudienceGroupRequest JSON.")] string file)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var json = await File.ReadAllTextAsync(file, CancellationToken.None);
            await _audience.AddUsersAsync(_runtime.Resolve(g), json, CancellationToken.None);
            Console.WriteLine("added users");
        });
    }

    [Command("delete", Description = "Delete an audience group.")]
    public Task<int> Delete(GlobalOptions g, [Argument(Description = "Audience group id.")] long audienceGroupId)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            await _audience.DeleteAsync(_runtime.Resolve(g), audienceGroupId, CancellationToken.None);
            Console.WriteLine($"deleted {audienceGroupId}");
        });
    }

    [Command("upload-file", Description = "Create an audience group by uploading user IDs from a text file (one ID/IFA per line).")]
    public Task<int> UploadFile(GlobalOptions g,
        [Option("file", Description = "Path to the text file of user IDs/IFAs (one per line).")] string file,
        [Option("description", Description = "Audience name (max 120 chars).")] string? description = null,
        [Option("ifa", Description = "The file contains IFAs instead of user IDs.")] bool ifa = false,
        [Option("upload-description", Description = "Description registered for the upload job.")] string? uploadDescription = null)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var audienceGroupId = await _audience.UploadFileAsync(
                _runtime.Resolve(g), file, description, ifa, uploadDescription, CancellationToken.None);
            if (g.Json)
            {
                Json.Print(new { audienceGroupId });
            }
            else
            {
                Console.WriteLine($"created audienceGroupId = {audienceGroupId}");
            }
        });
    }

    [Command("add-file", Description = "Add user IDs from a text file to an existing group (one ID/IFA per line).")]
    public Task<int> AddFile(GlobalOptions g,
        [Argument(Description = "Audience group id.")] long audienceGroupId,
        [Option("file", Description = "Path to the text file of user IDs/IFAs (one per line).")] string file,
        [Option("upload-description", Description = "Description registered for the upload job.")] string? uploadDescription = null)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            await _audience.AddFileAsync(_runtime.Resolve(g), audienceGroupId, file, uploadDescription, CancellationToken.None);
            Console.WriteLine($"added users to {audienceGroupId}");
        });
    }
}
