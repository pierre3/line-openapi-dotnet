using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Line.OpenApi.MiniApp;
using Line.OpenApi.MiniApp.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Line.OpenApi.Tests;

// DI integration verification for LINE MINI App. Confirms that the MiniAppClient registered via
// AddLineMiniApp can be resolved, uses IHttpClientFactory, and is idempotent. Unlike Login, no
// required options exist (tokens are supplied per call), so there is no validation-failure case.
// No real HTTP calls are made.
public class MiniAppDiIntegrationTests
{
    [Fact]
    public void AddLineMiniApp_Resolves_WithoutConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLineMiniApp();
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<MiniAppClient>();
        Assert.NotNull(client);
    }

    [Fact]
    public void AddLineMiniApp_Registers_HttpClientFactory()
    {
        var services = new ServiceCollection();
        services.AddLineMiniApp();
        using var sp = services.BuildServiceProvider();

        var factory = sp.GetRequiredService<IHttpClientFactory>();
        using var http = factory.CreateClient(ServiceCollectionExtensions.HttpClientName);
        Assert.NotNull(http);
    }

    [Fact]
    public void AddLineMiniApp_Is_Idempotent()
    {
        var services = new ServiceCollection();
        services.AddLineMiniApp();
        services.AddLineMiniApp(); // the second call does not re-register

        Assert.Equal(1, services.Count(d => d.ServiceType == typeof(MiniAppClient)));
    }

    [Fact]
    public async Task AddLineMiniApp_AppliesAllowedHosts_FromOptions_ToActualRequests()
    {
        // Wire a recording handler into the named HttpClient so the DI-resolved MiniAppClient's
        // host gating (not just successful resolution) is verified end-to-end.
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"orderId\":\"O1\"}", System.Text.Encoding.UTF8, "application/json"),
        });
        var services = new ServiceCollection();
        services.AddLineMiniApp(o => o.AllowedHosts = new[] { "custom.example.com" });
        services.AddHttpClient(ServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var sp = services.BuildServiceProvider();

        var client = sp.GetRequiredService<MiniAppClient>();
        await client.ReserveProductAsync("USER-TOKEN", "203.0.113.1", "ios", "P1", "Name");

        // api.line.me is not in the configured allow list, so the token must be withheld.
        Assert.Equal("api.line.me", handler.Request!.RequestUri!.Host);
        Assert.Null(handler.Request.Headers.Authorization);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public RecordingHandler(HttpResponseMessage response) => _response = response;

        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(_response);
        }
    }
}
