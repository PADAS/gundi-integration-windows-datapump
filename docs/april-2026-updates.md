# Recent changes

This document records substantive changes made on the
`claude/crazy-villani-0b1f28` branch (PR #10) that future humans and AI
agents will need to understand before working on the codebase. It is
written for both audiences — concise enough to scan, but specific
enough to act on without re-reading the diffs.

The three topics, in order:

1. [Npgsql downgrade for PostgreSQL 10 compatibility](#1-npgsql-downgrade-for-postgresql-10-compatibility)
2. [Unit test additions (server error / circuit breaker / batching)](#2-unit-test-additions)
3. [Integration tests against real database snapshots](#3-integration-tests-against-real-database-snapshots)

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

## See also

- `unittests/Integration/README.md` — operator-facing env var docs.
- `app/DataPumpLib.cs` — reader and writer implementations.
- `service/RadioDataPumpService.cs` — service host that wires readers
  to writers.
- PR #10 commit history — authoritative source of what shipped.
