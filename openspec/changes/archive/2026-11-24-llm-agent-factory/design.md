# Design

## Context

See `proposal.md → Why` for motivation. The app currently wires a single `OllamaApiClient` singleton directly in `Program.cs`. All downstream consumers — `AiAnalyzerService`, `CareerAdvisorAgent`, `DataChatAgent`, and every other service — depend on `IChatClient` from `Microsoft.Extensions.AI`, so the abstraction is already in place. Only the construction site (`Program.cs`) and configuration need to change.

The existing `OllamaNumCtxChatClient` is a `DelegatingChatClient` defined inline in `Program.cs` that injects `num_ctx` into every Ollama request. It must remain Ollama-only; sending Ollama-specific options to OpenAI-compatible endpoints causes ignored but noisy payloads and can cause errors with strict servers.

## Goals / Non-Goals

**Goals:**
- Replace the hardcoded Ollama registration with a factory that selects the provider from config
- Support `Ollama`, `FreeModel` (freemodel.in), and `OpenAI` as valid providers
- Zero changes to any service, agent, or tool class
- Keep secrets out of committed files

**Non-Goals:**
- Per-request or per-agent provider switching
- Streaming response support changes (not in scope)
- Load balancing or fallback between providers
- Supporting providers beyond the three named above

## Decisions

### D1: `IChatClientFactory` interface + `LlmClientFactory` class

**Decision:** Introduce a thin `IChatClientFactory` interface with a single `Create()` method, implemented by `LlmClientFactory`, placed under `Infrastructure/`.

**Rationale:** Keeps `Program.cs` clean, makes the factory independently testable, and follows the existing DI pattern in the project. The factory is registered as a singleton and called once at startup to produce the `IChatClient` singleton.

**Alternative considered:** Extension method on `IServiceCollection` (e.g., `AddLlmClient()`). Rejected because it hides the factory entirely in startup wiring, making it harder to test or extend later.

```
IServiceCollection
    |
    +-- LlmClientFactory (singleton)
    |     reads IConfiguration
    |     + "Ollama"    -> OllamaApiClient + OllamaNumCtxChatClient wrapper
    |     + "FreeModel" -> OpenAIClient (custom BaseUrl) + TransientRetryChatClient wrapper
    |     + "OpenAI"    -> OpenAIClient (default endpoint) + TransientRetryChatClient wrapper
    |
    +-- IChatClient (singleton, produced by factory.Create())
          |
          v (injected into all consumers unchanged)
    AiAnalyzerService, CareerAdvisorAgent, DataChatAgent,
    CareerChatService, InterviewGeneratorService, ...
```

### D2: `Microsoft.Extensions.AI.OpenAI` for cloud providers

**Decision:** Use the `Microsoft.Extensions.AI.OpenAI` NuGet package for both FreeModel and OpenAI backends.

**Rationale:** freemodel.in is OpenAI-compatible (same `/v1/chat/completions` contract). The package wraps the official `OpenAI` .NET SDK and returns an `IChatClient` directly — no adapter code needed. `OllamaSharp` is retained for the Ollama path.

**Alternative considered:** Raw `HttpClient` calling the OpenAI-compatible endpoint. Rejected — more code to maintain, no benefit given the package already handles retries, serialization, and auth.

### D3: Configuration shape

**Decision:** Nest all LLM config under a single `Llm` section with `Provider` as the selector and one sub-section per provider.

```json
{
  "Llm": {
    "Provider": "FreeModel",
    "Ollama": {
      "Endpoint": "http://localhost:11434",
      "Model": "llama3.2:latest",
      "NumCtx": 8192
    },
    "FreeModel": {
      "BaseUrl": "https://freemodel.in/api",
      "Model": "gpt-4o-mini",
      "ApiKey": ""
    },
    "OpenAI": {
      "Model": "gpt-4o-mini",
      "ApiKey": ""
    }
  }
}
```

`ApiKey` fields are left empty in committed config. .NET User Secrets or environment variables (`Llm__FreeModel__ApiKey`, `Llm__OpenAI__ApiKey`) override them at runtime.

**Alternative considered:** A single flat `Provider`, `Model`, `Endpoint`, `ApiKey` structure. Rejected — doesn't scale to three providers with different shapes (Ollama has `NumCtx`, FreeModel has `BaseUrl`, etc.) and forces re-configuration when switching.

### D4: `OllamaNumCtxChatClient` scope

**Decision:** Move `OllamaNumCtxChatClient` from `Program.cs` into the factory (or keep inline) and apply it only inside the `"Ollama"` branch of `LlmClientFactory.Create()`.

**Rationale:** Prevents Ollama-specific options from leaking to cloud providers. The class remains a `DelegatingChatClient`; no functional change.

### D5: Fail-fast at startup for misconfiguration

**Decision:** `LlmClientFactory.Create()` throws `InvalidOperationException` immediately for unknown providers or missing API keys.

**Rationale:** Misconfiguration surfaced at startup (before any request) is far preferable to a mysterious 401 or null-reference at runtime under load.

### D6: `TransientRetryChatClient` middleware for cloud providers

**Decision:** Wrap the `IChatClient` returned by the FreeModel and OpenAI branches with `TransientRetryChatClient` — a `DelegatingChatClient` that retries HTTP 429 and 5xx failures with exponential backoff (5s → 15s → 30s, 4 attempts total) and throws `LlmProviderUnavailableException` when exhausted. The Ollama branch is not wrapped.

**Rationale:** Cloud providers (freemodel.in in particular) have capacity limits and occasional maintenance windows. A silent retry layer absorbs short-lived blips without surfacing errors to the caller. `DelegatingChatClient` is the idiomatic `Microsoft.Extensions.AI` extension point — no changes to any service or agent are needed.

**Alternative considered:** Polly resilience pipeline. Rejected — adds a dependency (`Microsoft.Extensions.Http.Resilience`) that isn't present elsewhere in the project; the hand-rolled retry loop is straightforward enough and keeps the dependency footprint minimal.

## Risks / Trade-offs

| Risk | Mitigation |
|---|---|
| freemodel.in `Temperature = 0.0f` rejection (some providers floor at 0.001) | Test at startup or document minimum; `AiAnalyzerService` can be adjusted if needed |
| `Microsoft.Agents.AI.ChatClientAgent` compatibility with non-Ollama providers | `ChatClientAgent` uses `IChatClient` — should be transparent; validate with smoke test after wiring |
| User Secrets not available in Docker / production | Document `Llm__FreeModel__ApiKey` env-var override pattern in `appsettings.Docker.json` comment |
| Retry backoff adds up to ~50s worst-case | Well within the existing 5-minute `HttpClient` timeout; acceptable for user-facing AI endpoints |

## Migration Plan

1. Add `Microsoft.Extensions.AI.OpenAI` package
2. Update `appsettings.json` and `appsettings.Docker.json` with the new `Llm` section (keep old `Ollama` section temporarily for reference, then remove)
3. Create `Infrastructure/LlmClientFactory.cs`
4. Replace Ollama wiring in `Program.cs` with factory registration
5. Run `dotnet user-secrets init` and set dev keys
6. Smoke-test all endpoints with FreeModel provider active
7. Remove the now-unused old `Ollama` top-level config key
