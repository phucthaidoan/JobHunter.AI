# Tasks

## 1. Package & Configuration

- [x] 1.1 Add `Microsoft.Extensions.AI.OpenAI` NuGet package to `JobHunter.AI.Api.csproj`
- [x] 1.2 Replace the `Ollama` top-level config section in `appsettings.json` with the new `Llm` section (Provider, Ollama, FreeModel, OpenAI sub-sections); leave `ApiKey` fields empty
- [x] 1.3 Apply the same `Llm` config section update to `appsettings.Docker.json`
- [x] 1.4 Run `dotnet user-secrets init` for the project (if not already initialised) and document the two secrets to set: `Llm:FreeModel:ApiKey` and `Llm:OpenAI:ApiKey`

## 2. LlmClientFactory

- [x] 2.1 Create `JobHunter.AI.Api/Infrastructure/IChatClientFactory.cs` — interface with `IChatClient Create()` method
- [x] 2.2 Create `JobHunter.AI.Api/Infrastructure/LlmClientFactory.cs` — implements `IChatClientFactory`, reads `Llm:Provider` from `IConfiguration`, and constructs the matching client:
  - `"Ollama"` → `OllamaApiClient` wrapped with `OllamaNumCtxChatClient`
  - `"FreeModel"` → `OpenAIClient` with `Llm:FreeModel:BaseUrl` and `Llm:FreeModel:ApiKey`
  - `"OpenAI"` → `OpenAIClient` with default endpoint and `Llm:OpenAI:ApiKey`
  - anything else → `throw new InvalidOperationException($"Unknown LLM provider: {provider}")`
- [x] 2.3 Add startup guard: throw `InvalidOperationException` if `Llm:FreeModel:ApiKey` or `Llm:OpenAI:ApiKey` is null/empty when that provider is selected

## 3. Program.cs Wiring

- [x] 3.1 Remove the hardcoded `OllamaApiClient` singleton and the `ollamaEndpoint` / `ollamaModel` / `numCtx` variables from `Program.cs`
- [x] 3.2 Register `LlmClientFactory` as `IChatClientFactory` singleton in DI
- [x] 3.3 Register `IChatClient` singleton via `sp => sp.GetRequiredService<IChatClientFactory>().Create()`
- [x] 3.4 Move `OllamaNumCtxChatClient` class into the factory file (or keep inline in `Program.cs` — either is fine) and ensure it is only applied in the Ollama branch

## 4. Validation & Smoke Test

- [x] 4.1 Run `dotnet build` and confirm zero errors
- [x] 4.2 Set `Llm:Provider` to `FreeModel` via User Secrets and confirm the application starts without error
- [x] 4.3 Send a request to `POST /api/analyze` and verify a valid response from freemodel.in
- [x] 4.4 Send a request to `POST /api/career-chat` and verify the agent responds correctly (tool calls working)
- [x] 4.6 Set `Llm:Provider` to an invalid value and confirm the application throws `InvalidOperationException` at startup with a clear message

## 5. Transient Retry Middleware

- [x] 5.1 Wire `TransientRetryChatClient` into the `FreeModel` and `OpenAI` branches of `LlmClientFactory` (requires injecting `ILogger` into the factory)
- [x] 5.2 Code review: confirm `TransientRetryChatClient` is **not** applied in the `Ollama` branch of `LlmClientFactory`
- [x] 5.3 Add unit tests covering retry behavior in `TransientRetryChatClientTests.cs` (success after transient failure, exhausted retries throw `LlmProviderUnavailableException`, non-transient errors propagate immediately)

## Workflow follow-up

- Archive the change after all tasks are verified: `openspec-archive-change`
