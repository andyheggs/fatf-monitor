# FATF Monitor

Standalone API-based monitor for FATF high-risk and monitored jurisdiction publications.

The monitor fetches the FATF pages for:

- Jurisdictions under Increased Monitoring
- High-Risk Jurisdictions subject to a Call for Action

It extracts the publication-details country lists, optionally asks OpenAI to review the extraction, compares new checks with the previous snapshot, and can run daily through GitHub Actions.

## Local Run

```powershell
copy .env.example .env
# add OPENAI_API_KEY to .env if you want LLM review
dotnet test FatfMonitor.slnx
dotnet run --project src\FatfMonitor.Api\FatfMonitor.Api.csproj
```

Endpoints:

- `GET /api/compliance/fatf/latest`
- `POST /api/compliance/fatf/check`

## GitHub Actions

Add `OPENAI_API_KEY` as a repository secret if LLM review should run in Actions.

The `FATF monitor` workflow runs tests, starts the API on a GitHub-hosted runner, checks the FATF sources, and uploads the JSON result as an artifact.
