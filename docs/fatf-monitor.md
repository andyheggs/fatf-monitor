# FATF daily monitor

The API monitors the latest FATF publications for:

- Jurisdictions under Increased Monitoring
- High-Risk Jurisdictions subject to a Call for Action

The primary retrieval path uses OpenAI hosted web search. The model searches for the latest FATF publications, returns strict JSON containing source URLs and jurisdiction lists, and the API stores/compares that structured result. If OpenAI web search is unavailable or returns invalid JSON, the API falls back to direct FATF homepage discovery and page extraction.

## API endpoints

- `GET /api/compliance/fatf/latest` fetches the current FATF lists without saving them.
- `POST /api/compliance/fatf/check` fetches the current FATF lists, compares them with the stored snapshot, saves the new snapshot, and returns added/removed jurisdictions.

## Configuration

```powershell
FatfMonitor__HomePageUrl=https://www.fatf-gafi.org/
FatfMonitor__IncreasedMonitoringUrl=
FatfMonitor__CallForActionUrl=
FatfMonitor__SnapshotPath=data/fatf-latest.json
FatfMonitor__CheckToken=
Llm__Provider=OpenAI
Llm__Model=gpt-4.1-mini
Llm__SearchModel=gpt-4.1-mini
OPENAI_API_KEY=
```

Keep real OpenAI API keys out of committed files. For local development, put `OPENAI_API_KEY` in a local `.env` file; `.env` is ignored by Git while `.env.example` remains safe to commit as a blank template. For hosted environments, use GitHub Actions secrets, Azure App Service configuration, or another managed secret store.

## GitHub hosting and maintenance

Use the included GitHub Actions workflow to run tests and perform the FATF check daily from a GitHub-hosted runner. Configure this repository secret:

- `OPENAI_API_KEY`: OpenAI key used for hosted web-search retrieval.

The workflow can also be run manually from the Actions tab. Each run uploads a `fatf-monitor-result` artifact containing the monitor JSON result, a short Markdown summary, source preflight diagnostics, and the API log.
