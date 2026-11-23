using JobHunter.AI.Api.Infrastructure;
using Microsoft.Extensions.Configuration;

// Test: unknown provider throws immediately
var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?> { ["Llm:Provider"] = "InvalidProvider" })
    .Build();

var factory = new LlmClientFactory(config);
try
{
    factory.Create();
    Console.WriteLine("FAIL: Expected InvalidOperationException");
}
catch (InvalidOperationException ex) when (ex.Message.Contains("InvalidProvider"))
{
    Console.WriteLine("PASS (4.6): InvalidOperationException thrown with provider name: " + ex.Message);
}

// Test: missing API key throws immediately
var config2 = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?> {
        ["Llm:Provider"] = "FreeModel",
        ["Llm:FreeModel:BaseUrl"] = "https://freemodel.in/api",
        ["Llm:FreeModel:ApiKey"] = ""
    })
    .Build();

var factory2 = new LlmClientFactory(config2);
try
{
    factory2.Create();
    Console.WriteLine("FAIL: Expected InvalidOperationException for missing key");
}
catch (InvalidOperationException ex) when (ex.Message.Contains("ApiKey"))
{
    Console.WriteLine("PASS (missing key): InvalidOperationException thrown: " + ex.Message);
}
