# FATF Monitor

Standalone API-based monitor for FATF high-risk and monitored jurisdiction publications.

The monitor tracks:

- Jurisdictions under Increased Monitoring
- High-Risk Jurisdictions subject to a Call for Action

The primary retrieval path uses OpenAI hosted web search to find the latest FATF publications, extract the jurisdiction lists, and return source URLs. If OpenAI web search is unavailable, the API falls back to direct FATF homepage/link discovery with `HttpClient`.

## Local Run

```powershell
copy .env.example .env
# add OPENAI_API_KEY to .env for hosted web-search retrieval
# Llm__SearchModel defaults to gpt-4.1-mini

dotnet test FatfMonitor.slnx
dotnet run --project src\FatfMonitor.Api\FatfMonitor.Api.csproj
```

Endpoints:

- `GET /api/compliance/fatf/latest`
- `POST /api/compliance/fatf/check`

## GitHub Actions

Add `OPENAI_API_KEY` as a repository secret. The workflow runs tests, starts the API on a GitHub-hosted runner, retrieves the latest FATF lists through OpenAI hosted web search, and uploads the JSON result as an artifact.
