# FATF daily monitor

The API now includes a FATF monitoring slice for:

- Jurisdictions under Increased Monitoring
- High-Risk Jurisdictions subject to a Call for Action

The monitor fetches the configured FATF source pages, extracts the publication-details country lists, optionally asks an LLM to review the extraction against source excerpts, compares the result with the previous saved snapshot, and stores the latest snapshot as JSON.

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
OPENAI_API_KEY=
```

If `OPENAI_API_KEY` is absent, the monitor still runs deterministic extraction and returns an LLM review status explaining that verification was skipped.

Keep real OpenAI API keys out of committed files. For local development, put `OPENAI_API_KEY` in a local `.env` file; `.env` is ignored by Git while `.env.example` remains safe to commit as a blank template. For hosted environments, use GitHub Actions secrets, Azure App Service configuration, or another managed secret store.

## GitHub hosting and maintenance

Use the included GitHub Actions workflow to run tests and perform the FATF check daily from a GitHub-hosted runner. This is useful when local or office network egress receives `403 Forbidden` from the FATF website.

Configure these repository secrets:

- `OPENAI_API_KEY`: optional OpenAI key used by the LLM review during the GitHub-runner check.
- `FATF_MONITOR_ENDPOINT`: the deployed `POST /api/compliance/fatf/check` URL.
- `FATF_MONITOR_TOKEN`: optional bearer token matching `FatfMonitor__CheckToken` if the endpoint is protected.

The workflow can also be run manually from the Actions tab. Each run uploads a `fatf-monitor-result` artifact containing the monitor JSON result, a short Markdown summary, and the API log.
