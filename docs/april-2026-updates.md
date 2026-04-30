# April 2026 updates

A snapshot of the substantive changes made to this codebase in April 2026
(PR #10 and follow-ups). Written so future-me can come back cold and
re-form a working mental model without re-reading every diff.

Topics:

1. [Npgsql downgrade for PostgreSQL 10 compatibility](#1-npgsql-downgrade-for-postgresql-10-compatibility)
2. [Unit test additions (server error / circuit breaker / batching)](#2-unit-test-additions)
3. [Integration tests against real database snapshots](#3-integration-tests-against-real-database-snapshots)
4. [Versioning, packaging, and GCS publish workflow](#4-versioning-packaging-and-gcs-publish-workflow)
5. [Configurable command timeout for slow databases](#5-configurable-command-timeout-for-slow-databases)

---

## 1. Npgsql downgrade for PostgreSQL 10 compatibility

### What changed

| File | Before | After |
| --- | --- | --- |
| `app/DataPump.csproj` | `Npgsql 8.0.5` | `Npgsql 6.0.11` |
| `service/RadioService.csproj` | `Npgsql 8.0.5` | `Npgsql 6.0.11` |

Commit: `91e7e63` — "Downgrade Npgsql from 8.0.5 to 6.0.11 for PostgreSQL 10 compatibility".

### Why

A production user runs PostgreSQL 10. Npgsql 8.x requires
PostgreSQL 12+ and refuses to connect to PG 10 at the protocol layer.
Npgsql 6.x supports PostgreSQL 9.6+, which covers PG 10. Versions
between (Npgsql 7.x) require PG 11+.

### Constraints this places on future work

- **Do not bump Npgsql past 6.x without first confirming the user has
  upgraded their database.** A casual "update all dependencies" PR will
  break their deployment.
- The PostgreSQL readers (`SmartDispatchPlusV1Reader`,
  `SmartOneDispatchReader` in `app/DataPumpLib.cs`) use
  `DateTime.SpecifyKind(..., DateTimeKind.Unspecified)` before binding
  timestamp parameters. This is required by Npgsql 6's stricter
  timestamp handling — Npgsql 6 throws on `DateTimeKind.Utc` or `Local`
  bound to a `timestamp without time zone` column. Do **not** "clean
  up" those `SpecifyKind` calls.
- If the project ever needs to support both PG 10 users and a feature
  that requires Npgsql 7+, the path forward is multi-targeting or a
  runtime fork — not a unilateral bump.

### How to test against PG 10

A PostgreSQL 10 container is the easiest way to validate before
shipping. Run on port 5433 to coexist with a local PG 5432:

```powershell
docker run -d --name pg10 `
  -e POSTGRES_PASSWORD=postgres `
  -e POSTGRES_DB=smartdispatch `
  -p 5433:5432 `
  postgres:10
```

Then load a dump (`pg_dump --no-owner --no-privileges`), and point an
integration test connection string at it. See
[Integration tests](#3-integration-tests-against-real-database-snapshots)
for the env-var pattern.

### Caveat: connection-string Port

The current PostgreSQL readers' constructors take `host` but no
explicit `port` parameter — they hardcode 5432. To exercise a
non-default port (e.g. 5433 in the container example above), either:

- Stop the local PG so the container can bind 5432; or
- Extend the reader constructors to accept a port (small, mechanical
  change in `app/DataPumpLib.cs`); or
- Use Docker port-forwarding tricks (host-network, etc.).

This limitation is also called out in
`unittests/Integration/README.md`.

---

## 2. Unit test additions

### What changed

`unittests/GpsLogTest.cs` grew from a single trivial smoke test to a
suite covering the `RadioDataPump` batching and error-handling paths.
These tests came in via the merge from `main` (PR #9 optimizations);
the claude branch picked them up at merge commit `d3877d7`. They are
documented here because the PR #9 work has not been documented
elsewhere and downstream changes (the integration tests below)
assume their existence.

Test classes in `unittests/GpsLogTest.cs`:

| Class | Purpose |
| --- | --- |
| `GpsLogTest` | Smoke test — `RadioDataPump` instantiates. |
| `RadioServiceConfigurationTest` | Smoke test — config object instantiates. |
| `RadioServiceTest` | All meaningful tests live here. See below. |

`RadioServiceTest` methods:

| Test | What it validates |
| --- | --- |
| `TestCursorAdvance` | Mock reader yields three records; cursor (`lower_date`) advances to the latest record's `cursor_at` after iteration. |
| `TestDataPump` | With `batchSize=25` and 3 records, `IDataWriter.PostObservations` is called exactly once with all 3 records (single batch flush). |
| `TestDataPumpBatching` | With `batchSize=2` and 3 records, two flushes occur: first of size 2, then of size 1. Verifies the partial-final-batch path. |
| `TestServerErrorSkipsBatch` | When `PostObservations` throws `HttpRequestException` (post-Polly-retry exhaustion, simulating 5xx), the pump catches it, skips the batch, and continues — does NOT propagate the exception. |
| `TestCircuitBreakerPausesProcessing` | When `PostObservations` throws `BrokenCircuitException`, the pump enters its 60-second pause; with a 2s `CancellationToken`, the pump exits via `OperationCanceledException`. The pump must propagate cancellation, not swallow it. |

### Mocking pattern

These tests use **Moq** + **AutoFixture**. Reader/writer mocks
implement `IDataReader` / `IDataWriter` from `app/DataPumpLib.cs`.
`MockResponse1()` and `MockResponse2()` (defined earlier in the file)
return `IAsyncEnumerable<ISourceRecord>` over hand-constructed records.

When writing further tests, **prefer this mocking pattern over
spinning up real DB connections** — that's what the integration tests
in section 3 are for.

### Constraints for future work

- The pump's contract is: **5xx after retries → skip batch and advance,
  cancellation → propagate, circuit-breaker open → pause until cancel
  or timeout**. Tests pin all three. Don't change the pump's
  exception-handling shape without updating these tests in tandem.
- `TestCircuitBreakerPausesProcessing` relies on a 60s breaker pause.
  If that constant moves, this test's `CancelAfter(2000)` may need
  adjustment.
- All tests in this file share no fixture state and parallelize fine.
  This is in contrast to the integration tests below.

### Running

```powershell
dotnet test unittests\DataPump.Tests.csproj `
  --filter "FullyQualifiedName!~DataPump.Tests.Integration"
```

The filter excludes the integration suite (which would otherwise skip
silently anyway when env vars are unset).

---

## 3. Integration tests against real database snapshots

### What changed

A new test suite under `unittests/Integration/` exercises each of the
four readers (`SmartDispatchPlusV1`, `SmartOneDispatch`, `KAS20`,
`TrbonetPlus`) against a real PostgreSQL or SQL Server instance.

Commit: `3c6266c` — "Add integration tests for reader SQL against real
databases".

| File | Role |
| --- | --- |
| `unittests/Integration/TestDbConfig.cs` | `PgTestConfig` and `MsSqlTestConfig` records. Parse the standard connection strings from env vars using `NpgsqlConnectionStringBuilder` / `SqlConnectionStringBuilder`. Return `null` if the env var is missing → test skips. |
| `unittests/Integration/DatabaseCollection.cs` | xUnit `[CollectionDefinition("Database", DisableParallelization = true)]` — serializes all DB tests. Also defines `ReaderTestsBase`, which deletes `state.json` in setup and dispose. |
| `unittests/Integration/PostgreSqlReaderTests.cs` | Tests for the two PG readers. |
| `unittests/Integration/SqlServerReaderTests.cs` | Tests for the two SQL Server readers. |
| `unittests/Integration/README.md` | Env var reference, setup notes, Testcontainers migration plan. |
| `unittests/run-integration-tests.ps1` | Tracked PowerShell runner with `CHANGE_ME` placeholders. **Do not put real credentials here.** |
| `.gitignore` | `*.local.ps1` ignored. Users copy the runner to `run-integration-tests.local.ps1` and edit that. |

### Env var contract

Tests gate on these. Any reader whose `CONNSTR` is unset / empty has
its tests **skipped** (not failed) via `[SkippableFact]` from
`Xunit.SkippableFact`. This keeps `dotnet test` green on CI and dev
machines without the databases.

| Reader | Connection string env var | Optional |
| --- | --- | --- |
| `SmartDispatchPlusV1` | `GUNDI_TEST_SMARTDISPATCH_CONNSTR` | `GUNDI_TEST_SMARTDISPATCH_SCHEMA` (default `public`) |
| `SmartOneDispatch` | `GUNDI_TEST_SMARTONE_CONNSTR` | `GUNDI_TEST_SMARTONE_SCHEMA` (default `public`) |
| `KAS20` | `GUNDI_TEST_KAS20_CONNSTR` | — |
| `TrbonetPlus` | `GUNDI_TEST_TRBONET_CONNSTR` | — |

Connection strings use the standard formats:

- PostgreSQL: `Host=localhost;Username=postgres;Password=...;Database=smartdispatch`
- SQL Server: `Data Source=localhost;Initial Catalog=kas20;User ID=sa;Password=...;TrustServerCertificate=True`

### What each test asserts

For every reader, two tests:

1. **`TestConnection_Succeeds`** — calls `reader.TestConnection()` and
   asserts `result.Success` is true. (Note PascalCase: `Success`,
   `Message`. Earlier drafts used lowercase and failed to compile.)
2. **`ReadNew_ExecutesQueryAndMapsRows`** — calls
   `reader.ReadNew(DateTime.UtcNow.AddYears(-20))`, iterates up to
   5 records, asserts each is non-null. Confirms the SQL parses, runs,
   and the row-mapping code path executes without exception.

`SmartDispatchPlusV1ReaderTests` has a third test —
`GetGroupAliases_ExecutesWithoutError` — covering the auxiliary
group-alias query specific to that reader.

### Why the suite is serialized

Each reader writes its high-water-mark state to a hardcoded
`state.json` in the working directory. Two readers running in parallel
would race on that file. The xUnit collection
`DisableParallelization = true` plus `ReaderTestsBase` (which deletes
`state.json` before and after every test) keeps each test isolated.

If you ever add a fifth reader, give its tests the same
`[Collection("Database")]` attribute and inherit `ReaderTestsBase`.

### Running

The expected workflow on a developer machine:

```powershell
# One-time, never committed:
Copy-Item .\unittests\run-integration-tests.ps1 .\unittests\run-integration-tests.local.ps1
notepad .\unittests\run-integration-tests.local.ps1   # fill in CONNSTRs
.\unittests\run-integration-tests.local.ps1
```

Or directly via `dotnet test` after exporting the env vars:

```powershell
$env:GUNDI_TEST_SMARTDISPATCH_CONNSTR = "Host=...;..."
dotnet test unittests\DataPump.Tests.csproj `
  --filter "FullyQualifiedName~DataPump.Tests.Integration"
```

### Constraints for future work

- **Do not commit real credentials.** `*.local.ps1` is gitignored;
  the tracked `run-integration-tests.ps1` is a template. If you add
  another secret, follow the same `.local.ps1` override pattern or
  switch to a vault.
- **Do not assume a DB is present.** Every integration test must use
  `[SkippableFact]` and call `Skip.If(cfg is null, ...)` at the top.
  A test that hard-fails on a missing env var breaks CI and dev
  workflows.
- **Tests must be read-only** (or roll back what they write). The DBs
  are user-provided and may be production snapshots.
- **Migration target: Testcontainers.** The README anticipates a move
  from "point at user-managed DBs" to "spin up containers per test
  run." Keep the reader constructors and `*TestConfig.FromEnv` shape
  flexible enough that the env-var mode can be deprecated cleanly.
- **Port limitation.** The PostgreSQL readers don't currently accept a
  port. See [section 1 caveat](#caveat-connection-string-port). Until
  fixed, integration tests can only target port 5432 on localhost.

---

## 4. Versioning, packaging, and GCS publish workflow

### What changed

Three things landed together:

| File | Role |
| --- | --- |
| `Version.props` (new, repo root) | Single source of truth for `<Version>`. |
| `publish.proj` (new) | MSBuild script that produces `publish/gundi-radio-service-<version>.zip`, stamping `git rev-parse --short HEAD` into `SourceRevisionId` so the runtime User-Agent becomes `Gundi Radio Service/<version>+<sha>`. |
| `publish-to-gcs.ps1` (new, repo root) | Uploads the zip to `gs://radio-connectors/`, runs `gsutil acl ch -u AllUsers:R`, prints the public URL. |
| `docs/building-and-publishing.md` (new) | Operator-facing walkthrough for the above. |

The User-Agent reflection lives in `VersionInfo` at the top of
`app/DataPumpLib.cs` — it reads `AssemblyInformationalVersionAttribute`,
which MSBuild auto-derives from `<Version>` plus optional
`SourceRevisionId`.

### Why

Before, the version was hardcoded to `"Gundi Radio Service/2.1"` in two
places, dev builds could not be told apart from release builds, and
publishing a zip required a sequence of clicks in Visual Studio.

### How to release

```powershell
# bump the one line in Version.props, commit
msbuild publish.proj
.\publish-to-gcs.ps1
```

Full details in `docs/building-and-publishing.md`. Don't forget to
authorize the SSH key for the PADAS GitHub org (SAML SSO) when pushing
from a new machine.

### Constraints for future work

- **`Version.props` is the only place to edit the version.** Both
  `app/DataPump.csproj` and `publish.proj` import it.
- **`run-integration-tests.ps1` refuses to run as itself.** It checks
  its own filename and throws if it hasn't been copied to `*.local.ps1`.
  This is to keep `CHANGE_ME` placeholder credentials from leaking into
  the caller's session env.

---

## 5. Configurable command timeout for slow databases

### What changed

| File | Change |
| --- | --- |
| `app/DataPumpLib.cs` | All four reader constructors now take `int commandTimeoutSeconds = 300` and append `Command Timeout=<n>` to the connection string. |
| `service/RadioDataPumpService.cs` | `RouteConfiguration` gained `CommandTimeoutSeconds` (default 300) and `ConnectionTimeoutSeconds` (default 30); both are passed to the reader constructors. |
| `Version.props` | Bumped to `2.3.0`. |

### Why

A user running PostgreSQL 10 reported the service failing on first run
with `Timeout during reading attempt` after every retry. Diagnosis: the
Smart One Dispatch reader's initial query (a 2-day lookback against
`dbo.gps_location_data_base` joined to `dbo.device_info`, sorted by
`receive_datetime`, limited to 1000) was hitting Npgsql's default
30-second command timeout because the table had no index on
`receive_datetime` — PG was sequentially scanning and sorting the entire
history table before it could stream the first row back.

### What this fixes vs. doesn't fix

- **Fixes**: anyone with a slow but eventually-responsive DB. Default
  jumps from 30s to 300s, which clears the reported failure mode.
- **Does not fix**: the underlying performance problem. The right
  permanent answer is an index on the timestamp column, e.g.
  `CREATE INDEX CONCURRENTLY idx_gps_location_data_base_receive_datetime
  ON dbo.gps_location_data_base (receive_datetime);` — this brings the
  query from minutes to milliseconds and stays fast as the table grows.

### Configuration

Operators can override the defaults in `appsettings.json`:

```json
"RouteConfiguration": {
    "CommandTimeoutSeconds": 600,
    "ConnectionTimeoutSeconds": 30
}
```

Both are optional. Omitted, you get 300 / 30.

### Constraints for future work

- The Configurator UI does not (yet) expose either timeout. Its
  test-connection queries are tiny so the default is fine, but if you
  add UI for any reader knob you'll probably want these too.
- The connection-string parameter is `Command Timeout` for both
  `Microsoft.Data.SqlClient` and `Npgsql 6`. If Npgsql is ever upgraded,
  re-verify the keyword still applies.

---

## See also

- `unittests/Integration/README.md` — operator-facing env var docs.
- `docs/building-and-publishing.md` — operator-facing release docs.
- `app/DataPumpLib.cs` — reader and writer implementations.
- `service/RadioDataPumpService.cs` — service host that wires readers
  to writers.
- PR #10 commit history — authoritative source of what shipped.
