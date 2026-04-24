# Database integration tests

These tests validate each reader's queries against a real database. They are
gated behind environment variables: when a reader's env var is not set, its
tests are skipped (not failed), so `dotnet test` works on any machine.

## Env vars

Each reader has a `_CONNSTR` env var holding a standard connection string for
the reader's dialect. PostgreSQL readers additionally use `_SCHEMA` (defaults
to `public`), which matches the `database_schema` parameter that the reader
uses in its `Search Path`.

| Reader                   | Connection string env var          | Schema env var (optional)       |
| ------------------------ | ---------------------------------- | ------------------------------- |
| `SmartDispatchPlusV1`    | `GUNDI_TEST_SMARTDISPATCH_CONNSTR` | `GUNDI_TEST_SMARTDISPATCH_SCHEMA` |
| `SmartOneDispatch`       | `GUNDI_TEST_SMARTONE_CONNSTR`      | `GUNDI_TEST_SMARTONE_SCHEMA`    |
| `KAS20`                  | `GUNDI_TEST_KAS20_CONNSTR`         | —                               |
| `TrbonetPlus`            | `GUNDI_TEST_TRBONET_CONNSTR`       | —                               |

### Example (PowerShell)

```powershell
$env:GUNDI_TEST_SMARTDISPATCH_CONNSTR = "Host=localhost;Username=postgres;Password=secret;Database=smartdispatch"
$env:GUNDI_TEST_SMARTDISPATCH_SCHEMA  = "dbo"
$env:GUNDI_TEST_KAS20_CONNSTR = "Data Source=localhost;Initial Catalog=kas20;User ID=sa;Password=secret;TrustServerCertificate=True"

dotnet test unittests/DataPump.Tests.csproj
```

Unset variables cause their tests to skip with a message naming the variable
to set.

## What the tests assert

Each reader has two (or three) tests:

- `TestConnection_Succeeds` — calls `reader.TestConnection()` and asserts success.
- `ReadNew_ExecutesQueryAndMapsRows` — iterates up to 5 records and asserts
  each materializes without throwing. Does not assert row counts or specific
  values (your snapshot may contain any number of rows).
- `GetGroupAliases_ExecutesWithoutError` (SmartDispatchPlusV1 only).

The point is to catch dialect/schema drift — column renames, type changes,
missing tables — not to verify business logic on specific rows.

## Non-default ports

The reader's connection string template is `Host={server};...` and does not
currently forward a `Port` field. For tests, use a PostgreSQL instance on the
default port 5432, or include `Port=` in your env var and note that the reader
will not honor it. (If this becomes an issue, the reader constructors can be
extended to accept a port.)

## Migrating to Testcontainers later

The tests read DB config through `TestDbConfig.FromEnv(...)`. To switch to
Testcontainers, start containers in a collection fixture, set the env vars
from the container's exposed host/port/credentials, and the tests run
unchanged.
