# Gundi Radio Service

A Windows Service that forwards radio location data from on-premise
dispatch consoles to [Gundi](https://www.earthranger.com/gundi) /
EarthRanger. Polls the dispatch console's database on a configured
schedule, posts new observations to the Gundi API, and exposes an
embedded local web UI for setup, monitoring, log viewing, and
in-place auto-updates.

## For operators

End-user documentation — install, first-run setup, day-to-day use,
updates, troubleshooting — lives at the public docs site:

**<https://padas.github.io/gundi-integration-windows-datapump/>**

The installer (signed via Velopack, auto-update enabled) is
available at
<https://storage.googleapis.com/radio-connectors/velopack/GundiRadioService-win.msi>.

## Supported data sources

| Source | Database | Reader |
|---|---|---|
| Kenwood KAS-20 dispatch | SQL Server | `KAS20DataReader` |
| Hytera Smart Dispatch Plus | PostgreSQL | `SmartDispatchPlusV1Reader` |
| Hytera SmartOne Dispatch | PostgreSQL | `SmartOneDispatchReader` |
| Trbonet Plus | SQL Server | `TrbonetPlusDataReader` |

Adding a source means implementing `IDataReader` (in `app/`) and
wiring it up in the configurator and service. The existing readers
in `app/DataPumpLib.cs` are the working examples to model after.

## Repository layout

| Path | What's there |
|---|---|
| `app/` | The core data-pump library (`DataPump.csproj`) — readers, writers, models, pump loop |
| `service/` | The Windows Service host (`RadioService.csproj`) — Worker service + embedded Blazor Server UI |
| `Configurator/` | Legacy WinForms configurator (predates the embedded web UI; retained for offline use) |
| `publish.proj` | MSBuild project that produces the Velopack release package |
| `publish-velopack.ps1` | Wraps `publish.proj` and the Velopack CLI for a full release |
| `publish-to-gcs.ps1` | Uploads the produced MSI to the public GCS bucket |
| `assets/installer/` | Icons, banner art, and other installer assets |
| `docs-site/` | Public-facing documentation (Material for MkDocs → GitHub Pages) |
| `docs/` | Internal engineering notes (not published) |
| `unittests/` | xUnit test project + integration-test PowerShell harness |
| `Version.props` | Single source of truth for the product version |

## Build (developers)

Requires the .NET 8 SDK on Windows.

```powershell
dotnet build service\RadioService.csproj -nologo
```

Zero errors required for a clean build. Some long-tail nullability
warnings are tolerated.

## Run locally (developers)

Two options:

**As a console app** — easiest for iteration. The service detects
non-Windows-Service launches and starts Kestrel + the pump in-process:

```powershell
dotnet run --project service\RadioService.csproj
```

UI binds to <http://localhost:47823>. The console window receives
log output directly.

**As an installed Windows Service** — closer to production. Build a
release MSI via `publish-velopack.ps1`, install it, and the service
runs under SCM. See `docs/building-and-publishing.md` for the full
walkthrough.

## Tests

Unit tests:

```powershell
dotnet test unittests\unittests.csproj
```

Integration tests target real databases via environment-variable
connection strings — see `unittests/run-integration-tests.local.ps1`
for the pattern. They are off by default in CI; run locally when
touching reader query logic.

## Contributing

Code changes land via pull request — see `CLAUDE.md` for the
branching, commit, and review conventions (PR-only flow, always
request Copilot review, clean `dotnet build`).

## License

Apache License 2.0. See [`LICENSE`](LICENSE) for the full text.
