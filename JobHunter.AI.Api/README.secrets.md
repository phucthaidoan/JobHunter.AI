# Developer Secrets Setup

API keys for cloud LLM providers are **never committed** to source control.  
Set them via [.NET User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) for local development, and via environment variables in production / Docker.

## Local Development (User Secrets)

```bash
# From the JobHunter.AI.Api directory:

dotnet user-secrets set "Llm:FreeModel:ApiKey" "<your-freemodel.in-api-key>"
dotnet user-secrets set "Llm:OpenAI:ApiKey"    "<your-openai-api-key>"
```

## Production / Docker (Environment Variables)

Pass secrets as environment variables using the double-underscore (`__`) separator for nested keys:

```
Llm__FreeModel__ApiKey=<your-freemodel.in-api-key>
Llm__OpenAI__ApiKey=<your-openai-api-key>
```

Add these to your `docker-compose.yml` under `environment:` or inject via your secrets manager.

## Switching Providers

Change `Llm:Provider` in `appsettings.json` (or override via env var `Llm__Provider`):

| Value       | Description                        |
|-------------|------------------------------------|
| `FreeModel` | freemodel.in (OpenAI-compatible)   |
| `OpenAI`    | OpenAI (api.openai.com)            |
| `Ollama`    | Local Ollama instance              |

No code changes required — only config.
