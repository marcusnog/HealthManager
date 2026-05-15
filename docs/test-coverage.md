# Test Coverage

Use this flow to generate a readable coverage report for the backend locally or in CI.

## Local commands

```powershell
dotnet test HealthManager.sln --collect:"XPlat Code Coverage" --results-directory ./TestResults
dotnet tool run reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:"CoverageReport" -reporttypes:"Html;Cobertura;MarkdownSummaryGithub"
```

## Output

- HTML report: `CoverageReport/index.html`
- Cobertura XML: `CoverageReport/Cobertura.xml`
- GitHub summary markdown: `CoverageReport/SummaryGithub.md`

## Notes

- The local tool manifest in `.config/dotnet-tools.json` includes `reportgenerator`.
- Delete `TestResults/` and `CoverageReport/` before re-running if you want a clean snapshot.
- CI uploads both raw test results and the generated coverage report as artifacts.
