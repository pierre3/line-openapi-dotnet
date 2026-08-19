**English** | [日本語](README_ja.md)

# Samples

Demo apps that show how to use the `Line.OpenApi.*` packages. **They run offline by default** (without environment variables they do not make real calls — they only show how requests are assembled). Set the environment variables to connect to the real LINE API.

| Project | Kind | Contents |
|---|---|---|
| `Line.OpenApi.Samples.Console` | Console | Send / LIFF management / token issuance / webhook parsing (offline) |
| `Line.OpenApi.Samples.Webhook` | minimal web api | Real webhook receiving → echo reply (live demo via a dev tunnel) |
| `Line.OpenApi.Samples.Login` | minimal web api | LINE Login + OpenID Connect: authorization-code flow (PKCE) → profile / friendship |
| `Line.OpenApi.Samples.Ai` | Console | LLM tool-calling: a scripted model drives `Line.OpenApi.Extensions.AI` tools through the safety gates (allow-list policy, approval hook) |

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
| `LINE_LOGIN_CHANNEL_ID` / `LINE_LOGIN_CHANNEL_SECRET` | LINE Login channel ID / secret | Login |
| `LINE_LOGIN_REDIRECT_URI` | Callback URL registered in the console (default `http://localhost:5000/callback`) | Login |
| `LLM_MODEL` / `LLM_API_KEY` | Model id + API key to drive the tools with a real model (unset → scripted) | AI |
| `LLM_BASE_URL` | Base URL of an OpenAI-compatible endpoint (e.g. `https://api.groq.com/openai/v1`); unset → OpenAI | AI |

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

> 💡 **Tip — skip the console for step 2/3.** With the [`line` CLI tool](../tools/README.md) you can set the URL and verify connectivity from the terminal, which is handy because a dev tunnel gets a new URL on every restart:
>
> ```powershell
> $env:LINE_CHANNEL_ACCESS_TOKEN = "<channel access token>"
> line webhook set-endpoint --url https://xxxx.devtunnels.ms/webhook
> line webhook test-endpoint    # asks LINE to probe the endpoint and reports reachability
> ```

---

## 3. LINE Login web app (`Line.OpenApi.Samples.Login`)

A minimal API that runs the LINE Login **authorization-code flow with PKCE** and, on the
callback, verifies the ID token (server-side, via LINE), then shows the user's profile and
friendship status. Unlike the webhook sample, LINE Login allows **localhost callbacks**, so no
dev tunnel is needed.

### 3-1. Register the redirect URI and set credentials

1. In the [LINE Developers Console](https://developers.line.biz/console/), open your **LINE Login** channel
2. Under **LINE Login settings**, add the callback URL `http://localhost:5000/callback`
3. Start the app:

```powershell
cd samples/Line.OpenApi.Samples.Login
$env:LINE_LOGIN_CHANNEL_ID     = "<login channel id>"
$env:LINE_LOGIN_CHANNEL_SECRET = "<login channel secret>"
# Optional: enables the deauthorize demo on /logout (Messaging channel access token)
$env:LINE_CHANNEL_ACCESS_TOKEN = "<messaging channel access token>"
dotnet run
```

- `GET /` … Home; shows whether Login is configured and a "Sign in with LINE" link
- `GET /login` … Builds the authorization URL (state + PKCE stored in the session) and redirects to LINE
- `GET /callback` … Verifies `state`, exchanges the code (`ExchangeCodeAsync`), verifies the ID token (`VerifyIdTokenAsync`), and shows the profile + friendship status
- `GET /logout` … Revokes the access token (and calls `DeauthorizeAsync` when a Messaging channel token is set)

> The app starts even without credentials (`GET /` reports "disabled"). It listens on the origin
> of `LINE_LOGIN_REDIRECT_URI` (default `http://localhost:5000`).

### 3-2. Try it

Open `http://localhost:5000/` and click **Sign in with LINE**. After consenting on LINE, you are
redirected back to `/callback`, which displays your userId, display name, picture, ID-token
claims, and whether you are a friend of the Official Account linked to the Login channel.

> Unlike the console sample, LINE Login cannot run offline: without credentials the app only
> shows a "disabled" page (the flow is a live browser round-trip). The sample uses
> `AddLineLogin` (DI). Beyond what it shows, the same `LoginClient` also offers
> `RefreshTokenAsync`, `VerifyAccessTokenAsync`, and `GetUserInfoAsync` (OIDC userinfo).

---

## 4. AI tools agent (`Line.OpenApi.Samples.Ai`)

A console app that exposes the LINE Messaging use case to an LLM as
[Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/) `AIFunction` tools
(`Line.OpenApi.Extensions.AI`) and drives them through the real `FunctionInvokingChatClient`
loop. It demonstrates the package's safety model: opt-in sending, an allow-list `SendPolicy`, a
human-in-the-loop `BeforeSend` approval hook, and read-only validation.

```powershell
cd samples/Line.OpenApi.Samples.Ai
dotnet run                     # offline: local stub transport, gates run, nothing leaves the machine
```

The sample has two independent axes:

- **Brain** — a deterministic `ScriptedChatClient` (no API key) by default, or a real model when `LLM_MODEL` + `LLM_API_KEY` are set.
- **Transport** — a local stub by default (gates run, nothing leaves the machine), or real delivery with `LINE_CHANNEL_ACCESS_TOKEN` + `--send`.

**Scripted (default)** plays three steps and is fully reproducible:

1. **Tool discovery** — prints the tools the model sees; note the safety gates are **not** among the arguments.
2. **Allowed send** — a push to an allow-listed user → `SendPolicy` allows → `BeforeSend` prompts for approval (auto-approved when input is piped) → the send completes.
3. **Blocked send** — a push to a non-allow-listed user → `SendPolicy` denies → the tool raises `LineSendRefusedException`, which is fed back so the model reports it could not send.

**Real model.** Set `LLM_MODEL` + `LLM_API_KEY` to start a chat REPL where the model itself decides when to call the tools — the safety gates run exactly the same. Works with OpenAI and any OpenAI-compatible endpoint via `LLM_BASE_URL` (Groq / Together / Ollama's OpenAI endpoint / vLLM / LM Studio, …); the endpoint and model must support tool/function calling.

```powershell
# OpenAI
$env:LLM_MODEL   = "gpt-4o"
$env:LLM_API_KEY = "<openai api key>"
dotnet run
# You: tell Uallowed0000000000000000000000000 the meeting is at 10:00

# Any OpenAI-compatible endpoint (example: Groq)
$env:LLM_BASE_URL = "https://api.groq.com/openai/v1"
$env:LLM_MODEL    = "llama-3.3-70b-versatile"
$env:LLM_API_KEY  = "<groq api key>"
dotnet run
```

Send for real (add to either brain):

```powershell
$env:LINE_CHANNEL_ACCESS_TOKEN = "<channel access token>"
$env:LINE_TO_USER_ID           = "<allow-listed recipient userId>"
dotnet run -- --send
```

- Offline mode uses a **local stub transport** (not dry-run) precisely so the gates run — dry-run would short-circuit before them. Real message content is passed to `SendPolicy` / `BeforeSend`; treat tool arguments as potential PII in your logs.
- On a denied send the tool raises `LineSendRefusedException`. `FunctionInvokingChatClient` catches it and feeds the error back to the model (so step 3 shows the model's reply); the sample also has a defensive `catch` for versions/configs where the exception surfaces to the caller instead. With a real model, note that the default `IncludeDetailedErrors=false` hands it a generic error rather than the exact refusal reason.
- Only the brain differs between modes — the `AsBuilder().UseFunctionInvocation()` wiring, the tool list, and the gates are identical. For Semantic Kernel, pass the same tools to `kernel.Plugins.AddFromFunctions("Line", tools)`. `Microsoft.Extensions.AI` / `Microsoft.Extensions.AI.OpenAI` are referenced by the **sample only**; the published `Line.OpenApi.Extensions.AI` depends on the Abstractions alone.

---

## Troubleshooting

### LINE Login sample

- **`400 invalid_request` / redirect error:** `LINE_LOGIN_REDIRECT_URI` does not exactly match a callback URL registered on the Login channel. They must be identical (scheme, host, port, path).
- **`state mismatch` on `/callback`:** the session cookie was lost (expired, or the browser did not send it). Retry from `/login`.
- **`friend of the linked OA` is always false:** the Login channel must have an Official Account linked to it for friendship status to be meaningful.

### Webhook sample

- **No reply arrives:** `LINE_CHANNEL_ACCESS_TOKEN` is unset, or the reply token expired (about 1 minute after issuance). Check that `reply` is `enabled` in `GET /`.
- **401 returned:** `LINE_CHANNEL_SECRET` does not match the channel's.
- **Verify button fails:** Check that the dev tunnel is running, that the URL ends with `/webhook`, and that it is exposed with `--allow-anonymous`.
