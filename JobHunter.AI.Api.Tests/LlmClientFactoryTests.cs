using JobHunter.AI.Api.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobHunter.AI.Api.Tests;

public class LlmClientFactoryTests
{
    private static LlmClientFactory BuildFactory(Dictionary<string, string?> values) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            NullLogger<LlmClientFactory>.Instance);

    [Fact]
    public void Create_UnknownProvider_ThrowsWithProviderName()
    {
        var factory = BuildFactory(new() { ["Llm:Provider"] = "InvalidProvider" });

        var ex = Assert.Throws<InvalidOperationException>(() => factory.Create());
        Assert.Contains("InvalidProvider", ex.Message);
    }

    [Fact]
    public void Create_MissingProvider_ThrowsWithGuidance()
    {
        var factory = BuildFactory(new()); // no Llm:Provider set

        var ex = Assert.Throws<InvalidOperationException>(() => factory.Create());
        Assert.Contains("Llm:Provider", ex.Message);
    }

    [Fact]
    public void Create_FreeModel_MissingApiKey_Throws()
    {
        var factory = BuildFactory(new()
        {
            ["Llm:Provider"]          = "FreeModel",
            ["Llm:FreeModel:BaseUrl"] = "https://freemodel.in/api",
            ["Llm:FreeModel:ApiKey"]  = ""    // empty → guard fires
        });

        var ex = Assert.Throws<InvalidOperationException>(() => factory.Create());
        Assert.Contains("ApiKey", ex.Message);
    }

    [Fact]
    public void Create_OpenAI_MissingApiKey_Throws()
    {
        var factory = BuildFactory(new()
        {
            ["Llm:Provider"]     = "OpenAI",
            ["Llm:OpenAI:ApiKey"] = ""    // empty → guard fires
        });

        var ex = Assert.Throws<InvalidOperationException>(() => factory.Create());
        Assert.Contains("ApiKey", ex.Message);
    }

    [Fact]
    public void Create_FreeModel_WithApiKey_ReturnsClient()
    {
        var factory = BuildFactory(new()
        {
            ["Llm:Provider"]          = "FreeModel",
            ["Llm:FreeModel:BaseUrl"] = "https://freemodel.in/api",
            ["Llm:FreeModel:Model"]   = "gpt-4o-mini",
            ["Llm:FreeModel:ApiKey"]  = "test-key"
        });

        var client = factory.Create();
        Assert.NotNull(client);
    }

    [Fact]
    public void Create_OpenAI_WithApiKey_ReturnsClient()
    {
        var factory = BuildFactory(new()
        {
            ["Llm:Provider"]      = "OpenAI",
            ["Llm:OpenAI:Model"]  = "gpt-4o-mini",
            ["Llm:OpenAI:ApiKey"] = "test-key"
        });

        var client = factory.Create();
        Assert.NotNull(client);
    }

    [Fact]
    public void Create_Ollama_ReturnsClient()
    {
        var factory = BuildFactory(new()
        {
            ["Llm:Provider"]       = "Ollama",
            ["Llm:Ollama:Endpoint"] = "http://localhost:11434",
            ["Llm:Ollama:Model"]   = "llama3.2:latest",
            ["Llm:Ollama:NumCtx"]  = "8192"
        });

        var client = factory.Create();
        Assert.NotNull(client);
    }
}
