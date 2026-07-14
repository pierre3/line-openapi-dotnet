using System.Net;
using Cocona;
using Line.OpenApi.Cli.Configuration;
using Line.OpenApi.Cli.Output;
using Line.OpenApi.Cli.Services;

namespace Line.OpenApi.Cli.Cli;

/// <summary>
/// <c>line webhook ...</c> — C. Webhook development helpers: a local receiver (<c>listen</c>),
/// offline signature verification (<c>verify</c>), and replay to a local app (<c>replay</c>).
/// </summary>
internal sealed class WebhookCommands
{
    private readonly CliRuntime _runtime;
    private readonly WebhookService _webhook;

    public WebhookCommands(CliRuntime runtime, WebhookService webhook)
    {
        _runtime = runtime;
        _webhook = webhook;
    }

    [Command("verify", Description = "Verify a stored webhook payload's signature and summarize its events.")]
    public Task<int> Verify(
        GlobalOptions g,
        [Option("body", Description = "Path to the raw request body file.")] string body,
        [Option("signature", Description = "The x-line-signature header value.")] string signature,
        [Option("secret", Description = "Channel secret override.")] string? secret = null)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var channelSecret = ResolveSecret(g, secret);
            var bytes = await File.ReadAllBytesAsync(body, CancellationToken.None);
            var result = await _webhook.VerifyAsync(channelSecret, bytes, signature, CancellationToken.None);
            if (g.Json)
            {
                Json.Print(result);
            }
            else
            {
                Console.WriteLine("signature  : valid");
                Console.WriteLine($"destination: {result.Destination ?? "n/a"}");
                Console.WriteLine($"events     : {(result.EventTypes.Count > 0 ? string.Join(", ", result.EventTypes) : "(none)")}");
            }
        });
    }

    [Command("replay", Description = "POST a stored payload to a local URL (no signature added; destination not validated).")]
    public Task<int> Replay(
        GlobalOptions g,
        [Option("body", Description = "Path to the raw request body file.")] string body,
        [Option("to", Description = "Destination URL (e.g. http://localhost:5000/webhook).")] string to)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            if (!Uri.TryCreate(to, UriKind.Absolute, out var target))
            {
                throw new MessageInputException($"Invalid destination URL: '{to}'.");
            }

            var bytes = await File.ReadAllBytesAsync(body, CancellationToken.None);
            var result = await _webhook.ReplayAsync(bytes, target, CancellationToken.None);
            if (g.Json)
            {
                Json.Print(result);
            }
            else
            {
                Console.WriteLine($"{result.StatusCode} {result.ReasonPhrase}");
            }
        });
    }

    [Command("listen", Description = "Run a local webhook receiver that verifies signatures and prints events.")]
    public Task<int> Listen(
        GlobalOptions g,
        [Option("port", Description = "Port to listen on.")] int port = 5000,
        [Option("secret", Description = "Channel secret override.")] string? secret = null)
    {
        return _runtime.ExecuteAsync(g, async () =>
        {
            var channelSecret = ResolveSecret(g, secret);
            var prefix = $"http://localhost:{port}/";

            using var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            listener.Start();

            Console.WriteLine($"listening on {prefix} (Ctrl+C to stop)");

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                listener.Stop();
            };

            try
            {
                while (!cts.IsCancellationRequested)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await listener.GetContextAsync().ConfigureAwait(false);
                    }
                    catch (HttpListenerException) when (cts.IsCancellationRequested)
                    {
                        break;
                    }

                    await HandleAsync(context, channelSecret, cts.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                Console.WriteLine("stopped.");
            }
        });
    }

    private async Task HandleAsync(HttpListenerContext context, string channelSecret, CancellationToken cancellationToken)
    {
        using var reader = new MemoryStream();
        await context.Request.InputStream.CopyToAsync(reader, cancellationToken).ConfigureAwait(false);
        var bytes = reader.ToArray();
        var signature = context.Request.Headers["x-line-signature"];

        int status;
        try
        {
            var result = await _webhook.VerifyAsync(channelSecret, bytes, signature, cancellationToken).ConfigureAwait(false);
            var stamp = DateTimeOffset.Now.ToString("HH:mm:ss");
            Console.WriteLine($"[{stamp}] ok  destination={result.Destination ?? "n/a"} events=[{string.Join(", ", result.EventTypes)}]");
            status = 200;
        }
        catch (Line.OpenApi.Messaging.Webhook.WebhookException ex)
        {
            Console.Error.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] rejected: {ex.Message}");
            status = 400;
        }

        context.Response.StatusCode = status;
        context.Response.Close();
    }

    private string ResolveSecret(GlobalOptions g, string? secretOverride)
    {
        var credentials = _runtime.Resolve(g, new CredentialOverrides { ChannelSecret = secretOverride });
        return credentials.RequireChannelSecret();
    }
}
