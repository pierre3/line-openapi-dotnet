namespace Line.OpenApi.Cli.Mcp;

/// <summary>
/// Server-scoped MCP options set from <c>line mcp</c> flags. <c>ReadOnly</c> is enforced at
/// registration time (mutating tools are not listed); <c>AllowSecretOutput</c> gates whether
/// <c>line_token_issue</c> may return the raw token (spec §4.5); <c>AllowRemoteReplay</c> gates
/// whether <c>line_webhook_replay</c> may target non-loopback URLs (SSRF mitigation — over MCP
/// the URL is chosen by the model, not a human).
/// </summary>
public sealed record McpToolOptions(bool ReadOnly, bool AllowSecretOutput, bool AllowRemoteReplay);
