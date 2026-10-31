using DocService.Core.Llm;
using DocService.Llm.Fake;

namespace DocService.Core.Tests.Llm;

public class FakeLlmClientTests
{
    private static LlmRequest Request(string user = "u") => new("system", user, null, 100);

    [Fact]
    public async Task Enqueue_ReturnsInOrder_AndRecordsRequests()
    {
        var ct = TestContext.Current.CancellationToken;
        var fake = new FakeLlmClient().Enqueue("first").Enqueue("second");

        var a = await fake.CompleteAsync(Request("one"), ct);
        var b = await fake.CompleteAsync(Request("two"), ct);

        Assert.Equal("first", a.Content);
        Assert.Equal("second", b.Content);
        Assert.Equal(2, fake.Requests.Count);
        Assert.Equal("two", fake.Requests[1].UserPrompt);
        Assert.Equal(new LlmUsage(100, 50), a.Usage);
        Assert.Equal("stop", a.FinishReason);
    }

    [Fact]
    public async Task EnqueueException_Throws()
    {
        var fake = new FakeLlmClient().EnqueueException(new LlmTimeoutException("slow"));

        await Assert.ThrowsAsync<LlmTimeoutException>(
            () => fake.CompleteAsync(Request(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EmptyQueue_NoResponder_Throws()
    {
        var fake = new FakeLlmClient();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fake.CompleteAsync(Request(), TestContext.Current.CancellationToken));
        Assert.Equal("FakeLlmClient queue is empty", ex.Message);
    }

    [Fact]
    public async Task EmptyQueue_UsesResponder()
    {
        var fake = new FakeLlmClient
        {
            Responder = r => new LlmResult("echo:" + r.UserPrompt, LlmUsage.Zero, "fake", "stop"),
        };

        var result = await fake.CompleteAsync(Request("hi"), TestContext.Current.CancellationToken);

        Assert.Equal("echo:hi", result.Content);
    }

    [Fact]
    public async Task EnqueueHang_HonoursCancellation()
    {
        var fake = new FakeLlmClient().EnqueueHang();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fake.CompleteAsync(Request(), cts.Token));
    }
}
