using JobHunter.AI.Api.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace JobHunter.AI.Api.Tests;

public class TransientRetryChatClientTests
{
    // -------------------------------------------------------------------------
    // Minimal PipelineResponse stub — avoids Moq on non-virtual members
    // -------------------------------------------------------------------------

    private sealed class FakePipelineResponse(int status) : PipelineResponse
    {
        public override int Status => status;
        public override string ReasonPhrase => status.ToString();
        public override Stream? ContentStream { get => null; set { } }
        public override BinaryData Content => BinaryData.Empty;
        protected override PipelineResponseHeaders HeadersCore =>
            new Mock<PipelineResponseHeaders>().Object;
        public override BinaryData BufferContent(CancellationToken cancellationToken) => BinaryData.Empty;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken) => ValueTask.FromResult(BinaryData.Empty);
        public override void Dispose() { }
    }

    private static ClientResultException MakeHttpException(int status) =>
        new($"HTTP {status}", new FakePipelineResponse(status), null!);

    private static (TransientRetryChatClient client, Mock<IChatClient> innerMock) Build()
    {
        var inner = new Mock<IChatClient>();
        var client = new TransientRetryChatClient(inner.Object, NullLogger.Instance);
        return (client, inner);
    }

    // -------------------------------------------------------------------------
    // Scenario: success on first try (no retry needed)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetResponseAsync_SuccessOnFirstAttempt_ReturnsResponse()
    {
        var (client, inner) = Build();
        var expected = new ChatResponse(new ChatMessage(ChatRole.Assistant, "hi"));

        inner.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await client.GetResponseAsync([], null, CancellationToken.None);

        Assert.Equal(expected, result);
        inner.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // -------------------------------------------------------------------------
    // Scenario: success after a transient failure (retry works)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task GetResponseAsync_TransientFailureThenSuccess_ReturnsAfterRetry(int statusCode)
    {
        var (client, inner) = Build();
        var expected = new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));
        var callCount = 0;

        inner.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, _, _) =>
            {
                callCount++;
                if (callCount == 1) throw MakeHttpException(statusCode);
                return Task.FromResult(expected);
            });

        // Use a cancelled-on-delay token to skip the actual wait time in tests.
        var result = await client.GetResponseAsync([], null, CancellationToken.None);

        Assert.Equal(expected, result);
        Assert.Equal(2, callCount); // first attempt failed, second succeeded
    }

    // -------------------------------------------------------------------------
    // Scenario: all retries exhausted → LlmProviderUnavailableException
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    public async Task GetResponseAsync_AllRetriesExhausted_ThrowsLlmProviderUnavailableException(int statusCode)
    {
        var (client, inner) = Build();

        inner.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(MakeHttpException(statusCode));

        var ex = await Assert.ThrowsAsync<LlmProviderUnavailableException>(
            () => client.GetResponseAsync([], null, CancellationToken.None));

        Assert.Contains(statusCode.ToString(), ex.Message);
        Assert.IsType<ClientResultException>(ex.InnerException);

        // 4 total attempts (1 initial + 3 retries)
        inner.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    // -------------------------------------------------------------------------
    // Scenario: non-transient errors propagate immediately (no retry)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(404)]
    public async Task GetResponseAsync_NonTransientError_PropagatesImmediatelyWithoutRetry(int statusCode)
    {
        var (client, inner) = Build();

        inner.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(MakeHttpException(statusCode));

        await Assert.ThrowsAsync<ClientResultException>(
            () => client.GetResponseAsync([], null, CancellationToken.None));

        // Only one attempt — no retry for non-transient errors
        inner.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // -------------------------------------------------------------------------
    // Scenario: non-ClientResultException propagates immediately (no retry)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetResponseAsync_NonHttpException_PropagatesImmediately()
    {
        var (client, inner) = Build();

        inner.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("not an HTTP error"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetResponseAsync([], null, CancellationToken.None));

        inner.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
