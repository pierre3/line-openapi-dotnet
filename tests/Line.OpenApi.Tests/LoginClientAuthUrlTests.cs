using System;
using System.Collections.Generic;
using System.Web;
using Line.OpenApi.Login;
using Xunit;

namespace Line.OpenApi.Tests;

// Verifies BuildAuthorizationUrl composes access.line.me/oauth2/v2.1/authorize with the correct
// query parameters (no HTTP call is made).
public class LoginClientAuthUrlTests
{
    private static LoginClient NewClient() => new("1234567890", "secret");

    [Fact]
    public void BuildAuthorizationUrl_ComposesRequiredParameters()
    {
        var url = NewClient().BuildAuthorizationUrl(new AuthorizationUrlParameters
        {
            RedirectUri = "https://app.example.com/callback",
            Scopes = new[] { "openid", "profile" },
            State = "STATE-123",
        });

        Assert.StartsWith("https://access.line.me/oauth2/v2.1/authorize?", url);
        var query = HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("1234567890", query["client_id"]);
        Assert.Equal("https://app.example.com/callback", query["redirect_uri"]);
        Assert.Equal("STATE-123", query["state"]);
        Assert.Equal("openid profile", query["scope"]);
    }

    [Fact]
    public void BuildAuthorizationUrl_IncludesOptionalParameters()
    {
        var url = NewClient().BuildAuthorizationUrl(new AuthorizationUrlParameters
        {
            RedirectUri = "https://app/cb",
            Scopes = new[] { "openid" },
            State = "S",
            Nonce = "NONCE",
            CodeChallenge = "CHALLENGE",
            Prompt = "consent",
            BotPrompt = "aggressive",
            UiLocales = "ja-JP",
            ResponseMode = "form_post",
        });

        var query = HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.Equal("NONCE", query["nonce"]);
        Assert.Equal("CHALLENGE", query["code_challenge"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("consent", query["prompt"]);
        Assert.Equal("aggressive", query["bot_prompt"]);
        Assert.Equal("ja-JP", query["ui_locales"]);
        Assert.Equal("form_post", query["response_mode"]);
    }

    [Fact]
    public void BuildAuthorizationUrl_OmitsCodeChallenge_WhenAbsent()
    {
        var url = NewClient().BuildAuthorizationUrl(new AuthorizationUrlParameters
        {
            RedirectUri = "https://app/cb",
            Scopes = new[] { "profile" },
            State = "S",
        });

        var query = HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.Null(query["code_challenge"]);
        Assert.Null(query["code_challenge_method"]);
    }

    [Fact]
    public void BuildAuthorizationUrl_UrlEncodes_RedirectUri()
    {
        var url = NewClient().BuildAuthorizationUrl(new AuthorizationUrlParameters
        {
            RedirectUri = "https://app/cb?x=1&y=2",
            Scopes = new[] { "profile" },
            State = "S",
        });

        // The raw redirect_uri's reserved characters must be percent-encoded so they do not
        // leak into the outer query string.
        Assert.Contains("redirect_uri=https%3A%2F%2Fapp%2Fcb%3Fx%3D1%26y%3D2", url);
        var query = HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.Equal("https://app/cb?x=1&y=2", query["redirect_uri"]);
    }

    [Fact]
    public void BuildAuthorizationUrl_Validates_Inputs()
    {
        var client = NewClient();
        Assert.Throws<ArgumentException>(() => client.BuildAuthorizationUrl(new AuthorizationUrlParameters
        {
            RedirectUri = "",
            Scopes = new[] { "profile" },
            State = "S",
        }));
        Assert.Throws<ArgumentException>(() => client.BuildAuthorizationUrl(new AuthorizationUrlParameters
        {
            RedirectUri = "https://app/cb",
            Scopes = Array.Empty<string>(),
            State = "S",
        }));
        Assert.Throws<ArgumentException>(() => client.BuildAuthorizationUrl(new AuthorizationUrlParameters
        {
            RedirectUri = "https://app/cb",
            Scopes = new[] { "profile" },
            State = "",
        }));
    }
}
