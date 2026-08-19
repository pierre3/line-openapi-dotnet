using Microsoft.Extensions.AI;

namespace Line.OpenApi.Samples.Ai;

/// <summary>
/// A deterministic stand-in for a real LLM. It replays a fixed script of assistant turns so the
/// sample runs offline with no API key: each call to <see cref="GetResponseAsync"/> returns the
/// next scripted turn. A real app would swap this for an actual provider (OpenAI / Azure OpenAI /
/// Ollama) — the surrounding <see cref="FunctionInvokingChatClient"/> wiring is identical.
///
/// The script alternates: an assistant turn that requests a tool call, then (after
/// FunctionInvokingChatClient invokes the tool and appends its result) an assistant turn with the
/// final text. That is exactly the message flow a real model produces.
/// </summary>
internal sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<ChatMessage> _turns;

    public ScriptedChatClient(IEnumerable<ChatMessage> scriptedTurns)
        => _turns = new Queue<ChatMessage>(scriptedTurns);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var next = _turns.Count > 0
            ? _turns.Dequeue()
            : new ChatMessage(ChatRole.Assistant, "(the model has nothing further to say)");
        return Task.FromResult(new ChatResponse(next));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("This scripted sample uses the non-streaming path only.");

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }

    /// <summary>Builds an assistant turn that requests a single tool call.</summary>
    public static ChatMessage ToolCall(string callId, string toolName, IDictionary<string, object?> arguments)
        => new(ChatRole.Assistant, new List<AIContent> { new FunctionCallContent(callId, toolName, arguments) });

    /// <summary>Builds an assistant turn with a final text answer (ends the function-invocation loop).</summary>
    public static ChatMessage FinalText(string text)
        => new(ChatRole.Assistant, text);
}
