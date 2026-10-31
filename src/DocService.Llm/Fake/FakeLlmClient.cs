using System.Collections.Concurrent;
using DocService.Core.Llm;

namespace DocService.Llm.Fake;

/// <summary>Deterministic client: plays back scripted responses and records every request.</summary>
public sealed class FakeLlmClient : ILlmClient
{
    private static readonly LlmUsage DefaultUsage = new(100, 50);

    private readonly ConcurrentQueue<Func<CancellationToken, Task<LlmResult>>> _script = new();
    private readonly List<LlmRequest> _requests = [];
    private readonly Lock _gate = new();

    public Func<LlmRequest, LlmResult>? Responder { get; set; }

    public IReadOnlyList<LlmRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    public FakeLlmClient Enqueue(string content, LlmUsage? usage = null, string finishReason = "stop")
    {
        var result = new LlmResult(content, usage ?? DefaultUsage, "fake", finishReason);
        _script.Enqueue(_ => Task.FromResult(result));
        return this;
    }

    public FakeLlmClient EnqueueException(Exception exception)
    {
        _script.Enqueue(_ => Task.FromException<LlmResult>(exception));
        return this;
    }

    public FakeLlmClient EnqueueHang()
    {
        _script.Enqueue(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        return this;
    }

    public Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _requests.Add(request);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (_script.TryDequeue(out var step))
        {
            return step(cancellationToken);
        }

        if (Responder is { } responder)
        {
            return Task.FromResult(responder(request));
        }

        throw new InvalidOperationException("FakeLlmClient queue is empty");
    }
}
