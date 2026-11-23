using Microsoft.Extensions.AI;
using System.ClientModel;

namespace JobHunter.AI.Api.Infrastructure;

/// <summary>
/// Thrown when the LLM provider keeps returning transient failures (429/5xx)
/// after all retry attempts are exhausted. Carries a user-presentable message
/// (see the Problem detail returned by the AI endpoints).
/// </summary>
public sealed class LlmProviderUnavailableException(string message, Exception innerException)
    : Exception(message, innerException);

/// <summary>
/// Retries transient LLM provider failures (HTTP 429 and 5xx, e.g. "overloaded"
/// or "under maintenance") with exponential backoff, so short-lived capacity
/// blips on the provider side do not surface as hard errors. Once all attempts
/// are exhausted, throws <see cref="LlmProviderUnavailableException"/> with the
/// provider's own reason included.
/// </summary>
internal sealed class TransientRetryChatClient(IChatClient innerClient, ILogger logger)
    : DelegatingChatClient(innerClient)
{
    // 4 attempts total: immediate + retries after 5s, 15s, 30s (~50s of backoff).
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30)
    ];

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var totalAttempts = RetryDelays.Length + 1;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await base.GetResponseAsync(chatMessages, options, cancellationToken);
            }
            catch (ClientResultException ex) when (IsTransient(ex))
            {
                if (attempt >= totalAttempts)
                    throw new LlmProviderUnavailableException(
                        $"The LLM provider is temporarily unavailable (HTTP {ex.Status}) " +
                        $"after {totalAttempts} attempts. {ex.Message}", ex);

                var delay = RetryDelays[attempt - 1];
                logger.LogWarning(
                    "LLM call failed with transient error (HTTP {Status}), attempt {Attempt}/{Total}; retrying in {Seconds}s",
                    ex.Status, attempt, totalAttempts, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static bool IsTransient(ClientResultException ex) =>
        ex.Status == 429 || (ex.Status >= 500 && ex.Status < 600);
}
