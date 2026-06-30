# FATF Monitor

Standalone API-based monitor for FATF high-risk and monitored jurisdiction publications.

The monitor tracks:

- Jurisdictions under Increased Monitoring
- High-Risk Jurisdictions subject to a Call for Action

The primary retrieval path makes one OpenAI hosted web-search request to find the latest FATF publications and extract both jurisdiction lists. Results must pass publication-date, source, and minimum-list-size validation. If OpenAI returns a stale or invalid result, the API deterministically parses the current official HM Treasury FATF advisory on GOV.UK before attempting direct FATF access.

## Local Run

```powershell
copy .env.example .env
# add OPENAI_API_KEY to .env for hosted web-search retrieval
# Llm__SearchModel defaults to gpt-4.1-mini

dotnet test FatfMonitor.slnx
dotnet run --project src\FatfMonitor.Api\FatfMonitor.Api.csproj
```

The OpenAI key enables the primary hosted search. The official GOV.UK fallback does not require a key and avoids the `403 Forbidden` commonly returned by direct requests to `fatf-gafi.org`.

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

Add `OPENAI_API_KEY` as a repository secret. The workflow runs tests, starts the API on a GitHub-hosted runner, retrieves and validates the latest FATF lists, uploads the JSON result as an artifact, and publishes the latest JSON to GitHub Pages.

The workflow runs once daily at `07:15 UTC` and makes one OpenAI web-search request per run. The OpenAI key remains inside GitHub Actions and is never included in GitHub Pages or the Netlify dashboard. Before publishing, both FATF lists must:

- Have the same ISO publication date.
- Come from either official FATF pages or the official HM Treasury FATF advisory.
- Belong to the latest expected February, June, or October publication cycle.
- Not have a future publication date.

If validation fails, the workflow fails closed and does not replace the last successfully published Pages dataset.

After GitHub Pages is enabled for this repository, external apps can read the latest published result from:

```text
https://andyheggs.github.io/fatf-monitor/latest.json
```

The Pages output is static JSON. Calling it does not call OpenAI and does not use additional OpenAI credits. Credits are only used when the scheduled or manually triggered GitHub Actions workflow refreshes the result.

## Web dashboard

The `web` directory contains a static dashboard for Netlify. It retrieves the published GitHub Pages dataset through the Netlify proxy at `/api/fatf`, then provides:

- Totals for both FATF classifications.
- Search and classification filters.
- Links to the official FATF source publications.
- CSV and JSON downloads.
- Copy-to-clipboard JSON.

Netlify configuration is defined in `netlify.toml`; no OpenAI key is needed by the dashboard because it reads the latest already-published result.
