# FATF daily monitor

The API monitors the latest FATF publications for:

- Jurisdictions under Increased Monitoring
- High-Risk Jurisdictions subject to a Call for Action

The primary retrieval path makes one OpenAI hosted web-search request. The model searches for the latest FATF publications and returns strict JSON containing publication dates, source URLs, and both jurisdiction lists. If that result is stale or invalid, the API deterministically parses the current official HM Treasury FATF advisory on GOV.UK before attempting direct FATF homepage discovery.

## API endpoints

- `GET /api/compliance/fatf/latest` fetches the current FATF lists without saving them.
- `GET /api/compliance/fatf/jurisdictions` fetches the current FATF lists and returns them grouped for external API consumers.
- `POST /api/compliance/fatf/check` fetches the current FATF lists, compares them with the stored snapshot, saves the new snapshot, and returns added/removed jurisdictions.

External apps should prefer `GET /api/compliance/fatf/jurisdictions`. It returns:

- `increasedMonitoring.jurisdictions`: the exact jurisdictions extracted from the latest FATF "Jurisdictions under Increased Monitoring" publication.
- `callForAction.jurisdictions`: the exact jurisdictions extracted from the latest FATF "High-Risk Jurisdictions subject to a Call for Action" publication.
- `sourceUrl` and `count` for each list.
- `totalJurisdictions` across both lists.

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

The scheduled workflow runs at `07:15 UTC` each day and makes no more than one OpenAI web-search request. It validates matching publication dates, the expected February/June/October cycle, and minimum list sizes before deployment. The official GOV.UK advisory is used when the search result fails validation. A stale, mismatched, or future-dated result fails the workflow and leaves the last valid Pages dataset in place.

The workflow also publishes static JSON to GitHub Pages:

```text
https://andyheggs.github.io/fatf-monitor/latest.json
```

External apps should use this GitHub Pages URL when they only need the latest FATF result. It is static JSON, so client calls do not trigger OpenAI usage. OpenAI credits are only used when GitHub Actions refreshes the result.
