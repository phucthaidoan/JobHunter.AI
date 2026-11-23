using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OpenAI;
using System.ClientModel;

namespace JobHunter.AI.Api.Infrastructure;

/// <summary>
/// Reads <c>Llm:Provider</c> from configuration and constructs the matching
/// <see cref="IChatClient"/>. Supported providers: <c>Ollama</c>, <c>FreeModel</c>,
/// <c>OpenAI</c>.
/// </summary>
public sealed class LlmClientFactory(IConfiguration configuration, ILogger<LlmClientFactory> logger) : IChatClientFactory
{
    public IChatClient Create()
    {
        var provider = configuration["Llm:Provider"]
                       ?? throw new InvalidOperationException(
                           "Llm:Provider is not configured. Set it to 'Ollama', 'FreeModel', or 'OpenAI'.");

        return provider switch
        {
            "Ollama"    => BuildOllamaClient(),
            "FreeModel" => BuildFreeModelClient(),
            "OpenAI"    => BuildOpenAiClient(),
            _           => throw new InvalidOperationException(
                               $"Unknown LLM provider: '{provider}'. " +
                               $"Valid values are 'Ollama', 'FreeModel', and 'OpenAI'.")
        };
    }

    // -------------------------------------------------------------------------
    // Ollama
    // -------------------------------------------------------------------------

    private IChatClient BuildOllamaClient()
    {
        var endpoint = configuration["Llm:Ollama:Endpoint"] ?? "http://localhost:11434";
        var model    = configuration["Llm:Ollama:Model"]    ?? "llama3.2:latest";
        var numCtx   = configuration.GetValue<int>("Llm:Ollama:NumCtx", 8192);

        var ollamaClient = new OllamaApiClient(new Uri(endpoint), model);

        return new ChatClientBuilder(ollamaClient)
            .Use(inner => new OllamaNumCtxChatClient(inner, numCtx))
            .Build();
    }

    // -------------------------------------------------------------------------
    // FreeModel (OpenAI-compatible, custom base URL)
    // -------------------------------------------------------------------------

    private IChatClient BuildFreeModelClient()
    {
        var baseUrl = configuration["Llm:FreeModel:BaseUrl"]
                      ?? throw new InvalidOperationException(
                          "Llm:FreeModel:BaseUrl is not configured.");

        var model  = configuration["Llm:FreeModel:Model"] ?? "gpt-6-luna";
        var apiKey = configuration["Llm:FreeModel:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "Llm:FreeModel:ApiKey is missing or empty. " +
                "Set it via 'dotnet user-secrets set \"Llm:FreeModel:ApiKey\" <key>' " +
                "or the environment variable Llm__FreeModel__ApiKey.");

        var openAiClient = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(baseUrl) });

        var chatClient = openAiClient.GetChatClient(model).AsIChatClient();

        return new ChatClientBuilder(chatClient)
            .Use(inner => new TransientRetryChatClient(inner, logger))
            .Build();
    }

    // -------------------------------------------------------------------------
    // OpenAI (standard endpoint)
    // -------------------------------------------------------------------------

    private IChatClient BuildOpenAiClient()
    {
        var model  = configuration["Llm:OpenAI:Model"] ?? "gpt-4o-mini";
        var apiKey = configuration["Llm:OpenAI:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "Llm:OpenAI:ApiKey is missing or empty. " +
                "Set it via 'dotnet user-secrets set \"Llm:OpenAI:ApiKey\" <key>' " +
                "or the environment variable Llm__OpenAI__ApiKey.");

        var openAiClient = new OpenAIClient(new ApiKeyCredential(apiKey));

        var chatClient = openAiClient.GetChatClient(model).AsIChatClient();

        return new ChatClientBuilder(chatClient)
            .Use(inner => new TransientRetryChatClient(inner, logger))
            .Build();
    }
}

// -----------------------------------------------------------------------------
// Ollama context-window middleware (Ollama provider only)
// -----------------------------------------------------------------------------

/// <summary>
/// Injects the <c>num_ctx</c> option into every Ollama request so the model
/// uses the configured context window size.
/// </summary>
internal sealed class OllamaNumCtxChatClient(IChatClient innerClient, int numCtx)
    : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ChatOptions();
        options.AdditionalProperties ??= new AdditionalPropertiesDictionary();

        if (!options.AdditionalProperties.ContainsKey("num_ctx"))
            options.AdditionalProperties["num_ctx"] = numCtx;

        return base.GetResponseAsync(chatMessages, options, cancellationToken);
    }
}
