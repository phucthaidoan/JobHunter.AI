# Proposal

## Why

The app currently hardcodes Ollama as the only LLM backend, requiring a locally running Ollama instance and making it impossible to switch to cloud-hosted models without code changes. Introducing an `IChatClientFactory` with config-driven provider selection allows the team to run any supported LLM (Ollama, freemodel.in, OpenAI) by changing a single config value.

## What Changes

- Add `Llm` configuration section to `appsettings.json` with `Provider`, `Ollama`, `FreeModel`, and `OpenAI` sub-sections.
- Introduce a new `IChatClientFactory` interface and `LlmClientFactory` implementation that reads `Llm:Provider` from `IConfiguration` and constructs the appropriate `IChatClient`.
- Replace the hardcoded `OllamaApiClient` singleton registration in `Program.cs` with a factory-driven registration.
- Add `Microsoft.Extensions.AI.OpenAI` NuGet package to support OpenAI-compatible endpoints (freemodel.in and OpenAI).
- Keep `OllamaNumCtxChatClient` middleware but apply it only when the active provider is Ollama.
- Add `TransientRetryChatClient` middleware that retries transient provider failures (HTTP 429 and 5xx) with exponential backoff for cloud providers; throws `LlmProviderUnavailableException` after all attempts are exhausted.
- API keys for cloud providers are stored via .NET User Secrets (`Llm:FreeModel:ApiKey`, `Llm:OpenAI:ApiKey`); they are never committed to source.
- No changes to any `Services/`, `Agents/`, or `Tools/` classes — they all consume `IChatClient` and are unaffected.

## Capabilities

### New Capabilities

- `llm-provider`: Config-driven LLM provider selection via a client factory — supports Ollama, freemodel.in, and OpenAI as interchangeable backends through a single `IChatClient` abstraction.

### Modified Capabilities

_(none — no existing spec-level behavior changes; all services continue to use `IChatClient` identically)_

## Impact

- **Code**: `Program.cs`, new `Infrastructure/LlmClientFactory.cs`, new `Infrastructure/TransientRetryChatClient.cs`
- **Config**: `appsettings.json` (Llm section), `appsettings.Docker.json` (Llm section)
- **Dependencies**: Add `Microsoft.Extensions.AI.OpenAI` NuGet package; `OllamaSharp` retained for Ollama provider
- **Secrets**: `.NET User Secrets` for `Llm:FreeModel:ApiKey` and `Llm:OpenAI:ApiKey`
- **No API surface changes**: all HTTP endpoints remain identical
