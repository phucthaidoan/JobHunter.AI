# Spec Delta

## Purpose

Provides config-driven selection of an LLM backend (Ollama, freemodel.in, or OpenAI) so the application can switch providers without code changes, using a single `IChatClient` abstraction throughout.

## ADDED Requirements

### Requirement: Config-driven provider selection
The system SHALL read `Llm:Provider` from application configuration and instantiate the matching `IChatClient` at startup. Valid values are `Ollama`, `FreeModel`, and `OpenAI`.

#### Scenario: Ollama provider selected
- **WHEN** `Llm:Provider` is set to `Ollama`
- **THEN** the application uses OllamaSharp to connect to the endpoint defined in `Llm:Ollama:Endpoint` with the model in `Llm:Ollama:Model`

#### Scenario: FreeModel provider selected
- **WHEN** `Llm:Provider` is set to `FreeModel`
- **THEN** the application uses an OpenAI-compatible client pointed at `Llm:FreeModel:BaseUrl` with the model in `Llm:FreeModel:Model` and authenticates using the API key from `Llm:FreeModel:ApiKey`

#### Scenario: OpenAI provider selected
- **WHEN** `Llm:Provider` is set to `OpenAI`
- **THEN** the application uses an OpenAI-compatible client pointed at the standard OpenAI endpoint with the model in `Llm:OpenAI:Model` and authenticates using the API key from `Llm:OpenAI:ApiKey`

#### Scenario: Unknown provider configured
- **WHEN** `Llm:Provider` is set to an unrecognised value
- **THEN** the application SHALL throw an `InvalidOperationException` at startup with a message naming the unrecognised provider value

### Requirement: API keys stored outside source control
API keys for cloud providers (FreeModel, OpenAI) SHALL NOT be stored in committed configuration files. They MUST be supplied via .NET User Secrets (development) or environment variables / secrets managers (production).

#### Scenario: API key supplied via User Secrets
- **WHEN** `Llm:FreeModel:ApiKey` or `Llm:OpenAI:ApiKey` is set through `dotnet user-secrets`
- **THEN** the factory reads the key from `IConfiguration` and uses it to authenticate requests to the provider

#### Scenario: API key absent for a cloud provider
- **WHEN** `Llm:Provider` is `FreeModel` or `OpenAI` and the corresponding `ApiKey` configuration value is null or empty
- **THEN** the application SHALL throw an `InvalidOperationException` at startup with a message indicating the missing key

### Requirement: Ollama context window middleware applied only for Ollama
The `OllamaNumCtxChatClient` middleware that injects the `num_ctx` option SHALL be applied only when the active provider is `Ollama`. Cloud providers SHALL receive a plain `IChatClient` without the Ollama-specific middleware.

#### Scenario: Ollama provider with num_ctx middleware
- **WHEN** `Llm:Provider` is `Ollama` and a request is processed
- **THEN** the `num_ctx` value from `Llm:Ollama:NumCtx` is included in the chat options sent to the Ollama server

#### Scenario: Cloud provider without num_ctx middleware
- **WHEN** `Llm:Provider` is `FreeModel` or `OpenAI` and a request is processed
- **THEN** no `num_ctx` property is added to the chat options

### Requirement: Single globally active provider
The application SHALL use one active LLM provider at a time, determined at startup from configuration. All `IChatClient` consumers — services, agents, and tools — SHALL receive the same provider instance via dependency injection without modification.

#### Scenario: All consumers receive the configured provider
- **WHEN** the application starts with a valid `Llm:Provider`
- **THEN** every injected `IChatClient` in services and agents resolves to the client built by the factory for that provider

### Requirement: Transient provider failure retry with exponential backoff
For cloud providers (FreeModel, OpenAI), the system SHALL automatically retry requests that fail with HTTP 429 (rate-limited) or HTTP 5xx (server error) using exponential backoff before surfacing an error. After all retry attempts are exhausted, the system SHALL throw `LlmProviderUnavailableException`.

#### Scenario: Transient failure retried
- **WHEN** a cloud provider returns HTTP 429 or 5xx on a request
- **THEN** the system retries the request after a delay, up to the maximum attempts, without surfacing an error to the caller

#### Scenario: All retries exhausted
- **WHEN** a cloud provider keeps returning HTTP 429 or 5xx across all retry attempts
- **THEN** the system throws `LlmProviderUnavailableException` with a message including the HTTP status and the number of attempts made

#### Scenario: Ollama provider not retried
- **WHEN** `Llm:Provider` is `Ollama` and a request fails
- **THEN** no retry middleware is applied — Ollama is local and transient cloud rate-limit errors do not apply
