# Security

## Allowed hosts

Every token provider carries an `AllowedHostsValidator` and **only attaches the access token to
hosts on its allow list**. On the negative path it returns an empty token rather than leaking the
credential. This is a safety net against cross-host leakage — for example if an HTTP redirect
would otherwise carry the `Authorization` header to a different host.

Defaults:

- Messaging: `api.line.me` and `api-data.line.me`.
- LIFF: `api.line.me` only (no data-plane host).

The allow list is injectable (`AllowedHosts` on the options types, or the provider constructors)
rather than hard-coded, to prepare for future hosts such as `manager.line.biz`.

> **Minimum runtime version.** `Microsoft.Kiota.Abstractions` must be `>= 1.22.0`, which contains
> the fix for the `RedirectHandler` cross-host secret-header leak (CVE-2026-44503 /
> GHSA-7j59-v9qr-6fq9, CVSS 7.0). The libraries pin the Kiota bundle at `2.0.0`, which inherits
> the fix, and the .NET 10 SDK's NuGet audit flags regressions transitively.

## Webhook signature validation

Incoming webhooks are authenticated with the `x-line-signature` header. Validation is **not part
of the OpenAPI spec**, so it is implemented by hand in
@Line.Core.Webhook.WebhookSignatureValidator:

- HMAC-SHA256 over the **raw request body bytes**, keyed by the channel secret, Base64-encoded.
- Compared against the header in **constant time** (`CryptographicOperations.FixedTimeEquals`) to
  avoid timing attacks.

Consequences for your code:

- Validate against the **raw bytes**, before any model binding or re-encoding — otherwise the
  signature will not match. @Line.Messaging.Webhook.WebhookRequestParser enforces this by taking
  `byte[]`.
- A missing or malformed header is treated as invalid (returns `false`).
- **Body size limits are your responsibility.** Enforce a raw-body cap upstream (for example
  ASP.NET Core's `MaxRequestBodySize`) to bound memory use.

## Secret handling

- This library **never handles signing keys**. For JWT-assertion token issuance, you produce the
  signed assertion through a factory and pass only the resulting string.
- Channel access tokens and channel secrets are supplied by you (via options or provider
  constructors) and are not persisted by the library.
