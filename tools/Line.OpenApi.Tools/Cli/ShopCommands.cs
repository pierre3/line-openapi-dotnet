using Cocona;
using Line.OpenApi.Tools.Services;

namespace Line.OpenApi.Tools.Cli;

/// <summary>
/// <c>line shop ...</c> — shop operations (mission sticker send).
/// </summary>
internal sealed class ShopCommands
{
    private readonly CliRuntime _runtime;
    private readonly ShopService _shop;

    public ShopCommands(CliRuntime runtime, ShopService shop)
    {
        _runtime = runtime;
        _shop = shop;
    }

    [Command("mission", Description = "Send a mission sticker to a user from a JSON request file.")]
    public Task<int> Mission(GlobalOptions g, [Option("file", Description = "Path to the MissionStickerRequest JSON.")] string file)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var json = await File.ReadAllTextAsync(file, CancellationToken.None);
            await _shop.SendMissionAsync(_runtime.Resolve(g), json, CancellationToken.None);
            Console.WriteLine("mission sticker sent");
        });
    }
}
