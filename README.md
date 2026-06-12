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
- `GET /api/compliance/fatf/jurisdictions`
- `POST /api/compliance/fatf/check`

`GET /api/compliance/fatf/jurisdictions` returns a stable JSON shape for external apps:

```json
{
  "checkedAt": "2026-06-12T13:36:09Z",
  "increasedMonitoring": {
    "category": "IncreasedMonitoring",
    "name": "Jurisdictions under Increased Monitoring",
    "sourceUrl": "https://www.fatf-gafi.org/...",
    "jurisdictions": ["Algeria", "Angola"],
    "count": 2
  },
  "callForAction": {
    "category": "CallForAction",
    "name": "High-Risk Jurisdictions subject to a Call for Action",
    "sourceUrl": "https://www.fatf-gafi.org/...",
    "jurisdictions": ["Democratic Republic of Korea", "Iran", "Myanmar"],
    "count": 3
  },
  "llmReview": {
    "enabled": true,
    "provider": "OpenAI",
    "model": "gpt-4.1-mini",
    "summary": "Retrieved latest FATF jurisdiction lists using OpenAI hosted web search.",
    "confidence": null
  },
  "totalJurisdictions": 5
}
```

## GitHub Actions

Add `OPENAI_API_KEY` as a repository secret. The workflow runs tests, starts the API on a GitHub-hosted runner, retrieves the latest FATF lists through OpenAI hosted web search, and uploads the JSON result as an artifact.
