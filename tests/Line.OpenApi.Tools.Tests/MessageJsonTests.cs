using Line.OpenApi.Tools.Services;
using Line.OpenApi.Messaging.Generated.Api.Models;
using Xunit;

namespace Line.OpenApi.Tools.Tests;

public sealed class MessageJsonTests
{
    [Fact]
    public async Task TextMessagesJson_escapes_special_characters_and_round_trips()
    {
        const string original = "he said \"hi\"\nbye";
        var json = MessageJson.TextMessagesJson(original);
        Assert.StartsWith("[{\"type\":\"text\"", json);

        // Correctness is proven by parsing back to the exact original text.
        var messages = await MessageJson.ParseMessagesAsync(json, CancellationToken.None);
        var text = Assert.IsType<TextMessage>(Assert.Single(messages));
        Assert.Equal(original, text.Text);
    }

    [Fact]
    public void WrapFlex_embeds_contents_and_alt_text()
    {
        var json = MessageJson.WrapFlex("{\"type\":\"bubble\"}", "alt");
        Assert.Contains("\"type\":\"flex\"", json);
        Assert.Contains("\"altText\":\"alt\"", json);
        Assert.Contains("\"contents\":{\"type\":\"bubble\"}", json);
    }

    [Fact]
    public async Task ParseMessagesAsync_deserializes_polymorphic_messages()
    {
        var json = "[{\"type\":\"text\",\"text\":\"hi\"},{\"type\":\"sticker\",\"packageId\":\"1\",\"stickerId\":\"2\"}]";
        var messages = await MessageJson.ParseMessagesAsync(json, CancellationToken.None);

        Assert.Equal(2, messages.Count);
        Assert.IsType<TextMessage>(messages[0]);
        Assert.IsType<StickerMessage>(messages[1]);
    }

    [Fact]
    public async Task ParseMessagesAsync_from_text_helper_round_trips()
    {
        var messages = await MessageJson.ParseMessagesAsync(MessageJson.TextMessagesJson("hello"), CancellationToken.None);
        var text = Assert.IsType<TextMessage>(Assert.Single(messages));
        Assert.Equal("hello", text.Text);
    }
}
