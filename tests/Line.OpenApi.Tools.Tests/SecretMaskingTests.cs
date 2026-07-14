using Line.OpenApi.Tools.Output;
using Xunit;

namespace Line.OpenApi.Tools.Tests;

public sealed class SecretMaskingTests
{
    [Theory]
    [InlineData(null, "<unset>")]
    [InlineData("", "<unset>")]
    public void Mask_unset_values(string? input, string expected) =>
        Assert.Equal(expected, SecretMasking.Mask(input));

    [Fact]
    public void Mask_shows_only_last_four_characters()
    {
        Assert.Equal("…6789", SecretMasking.Mask("0123456789"));
    }

    [Fact]
    public void Mask_short_value_fully_dotted()
    {
        Assert.Equal("•••", SecretMasking.Mask("abc"));
    }

    [Fact]
    public void Mask_does_not_leak_the_secret_prefix()
    {
        var masked = SecretMasking.Mask("supersecrettoken");
        Assert.DoesNotContain("supersecret", masked);
    }
}
