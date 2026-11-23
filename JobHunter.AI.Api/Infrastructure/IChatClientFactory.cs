using Microsoft.Extensions.AI;

namespace JobHunter.AI.Api.Infrastructure;

/// <summary>
/// Factory that constructs an <see cref="IChatClient"/> for the configured LLM provider.
/// </summary>
public interface IChatClientFactory
{
    /// <summary>
    /// Creates and returns an <see cref="IChatClient"/> for the active provider
    /// as determined by the application configuration (<c>Llm:Provider</c>).
    /// </summary>
    IChatClient Create();
}
