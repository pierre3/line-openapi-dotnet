**English** | [日本語](README_ja.md)

# Samples

Demo apps that show how to use the `Line.OpenApi.*` packages. **They run offline by default** (without environment variables they do not make real calls — they only show how requests are assembled). Set the environment variables to connect to the real LINE API.

| Project | Kind | Contents |
|---|---|---|
| `Line.OpenApi.Samples.Console` | Console | Send / LIFF management / token issuance / webhook parsing (offline) |
| `Line.OpenApi.Samples.Webhook` | minimal web api | Real webhook receiving → echo reply (live demo via a dev tunnel) |

> These samples are `IsPackable=false` and are not included in the NuGet packages. They reference `src/` as project references.

## Environment variables

| Variable | Purpose | Used by |
|---|---|---|
| `LINE_CHANNEL_ACCESS_TOKEN` | Long-lived channel access token | Console (send / LIFF) / Webhook (reply) |
| `LINE_TO_USER_ID` | Recipient user ID for push | Console (send) |
| `LINE_CHANNEL_SECRET` | Signature verification key | Webhook (receive) |
| `LINE_CHANNEL_ID` | Channel ID (iss/sub for token issuance) | Console (token) |
| `LINE_KID` | kid of the assertion signing key | Console (token) |
| `LINE_PRIVATE_KEY` / `LINE_PRIVATE_KEY_PATH` | RSA private key (PEM body / file path. **File path recommended**) | Console (token) |

> Injecting a private key inline via an environment variable can leak it through the process list or crash dumps, so `LINE_PRIVATE_KEY_PATH` (a file reference) is recommended.

---

## 1. Console (`Line.OpenApi.Samples.Console`)

```powershell
cd samples/Line.OpenApi.Samples.Console

# Interactive menu
dotnet run

# One-shot (send | liff | token | webhook)
dotnet run -- webhook          # Fully offline: sign → verify → parse the bundled payload
dotnet run -- send             # Show how the send request is assembled (sends for real if a token is set)
dotnet run -- liff             # List LIFF apps (fetches for real if a token is set)
dotnet run -- liff crud        # Demonstrates add/update/delete (be careful — it changes the channel)
dotnet run -- token            # Token issuance flow (issues for real if a signing key is set)
```

Example of a real send (PowerShell):

```powershell
$env:LINE_CHANNEL_ACCESS_TOKEN = "<channel access token>"
$env:LINE_TO_USER_ID           = "<recipient userId>"
dotnet run -- send
```

- The `webhook` scenario always works without credentials (it self-signs with a demo secret, then uses `WebhookRequestParser` to verify, deserialize, and branch on the event).
- The `token` scenario generates the JWT assertion signature (RS256) with `JwtAssertionBuilder` (inside the sample). Signing is application-specific, so it is not included in the library and is placed on the sample side.

---

## 2. Webhook receiving web app (`Line.OpenApi.Samples.Webhook`)

A minimal API that receives webhooks from LINE and echoes text messages back. Expose your local machine through a **dev tunnel** so the LINE platform can reach it.

### 2-1. Set credentials and start

```powershell
cd samples/Line.OpenApi.Samples.Webhook
$env:LINE_CHANNEL_SECRET       = "<channel secret>"        # Required for receiving (signature verification)
$env:LINE_CHANNEL_ACCESS_TOKEN = "<channel access token>"  # Needed for replies (receiving works without it)
dotnet run
```

- `GET /` … Health check (returns configuration status as JSON)
- `POST /webhook` … Signature verification + parsing. Echoes back on text messages. Bad signature → 401, bad body → 400, unset secret → 503.

> It starts even if the secret is unset (check the status with `GET /`). Replies require a token.

### 2-2. Expose with a dev tunnel

Example using the [dev tunnels CLI](https://learn.microsoft.com/azure/developer/dev-tunnels/) (assumes the app is listening on `http://localhost:5000`; check the port in the startup log):

```powershell
# First time only
devtunnel user login

# Expose the app's port with anonymous access
devtunnel host -p 5000 --allow-anonymous
```

Append `/webhook` to the displayed HTTPS forwarding URL (e.g. `https://xxxx.devtunnels.ms`) to get the webhook URL.

> The Dev Tunnels feature in Visual Studio / VS Code can expose it the same way.

> ⚠️ **Caution:** `--allow-anonymous` exposes your local machine to the internet. `POST /webhook` is protected by signature verification, but `GET /` discloses the configuration status (whether webhook/reply are enabled) without authentication. **Limit this to demo use and stop the tunnel when you are done** (`Ctrl+C`).

### 2-3. Set the webhook URL in the LINE Developers console

1. Open the target channel (Messaging API) in the [LINE Developers Console](https://developers.line.biz/console/)
2. **Messaging API settings** → set **Webhook URL** to `https://xxxx.devtunnels.ms/webhook`
3. Turn **Use webhook** ON and click **Verify** to check connectivity (it reaches `POST /webhook`, not `GET /`)
4. Add the bot as a friend and send text in the chat; success is when `echo: <text>` comes back

---

## Troubleshooting

- **No reply arrives:** `LINE_CHANNEL_ACCESS_TOKEN` is unset, or the reply token expired (about 1 minute after issuance). Check that `reply` is `enabled` in `GET /`.
- **401 returned:** `LINE_CHANNEL_SECRET` does not match the channel's.
- **Verify button fails:** Check that the dev tunnel is running, that the URL ends with `/webhook`, and that it is exposed with `--allow-anonymous`.
