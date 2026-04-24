# Template runner for the integration tests.
#
# DO NOT put real credentials in this file — it is tracked in git.
# Instead, copy it to a sibling file ending in .local.ps1 (gitignored)
# and edit that copy:
#
#     Copy-Item .\unittests\run-integration-tests.ps1 .\unittests\run-integration-tests.local.ps1
#     # edit run-integration-tests.local.ps1 with real connection strings
#     .\unittests\run-integration-tests.local.ps1
#
# Readers you don't want to exercise: leave their CONNSTR commented out or
# empty — those tests will skip automatically. Env vars set here only live
# for the duration of this script's child processes.

$ErrorActionPreference = 'Stop'

# --- PostgreSQL readers -----------------------------------------------------

$env:GUNDI_TEST_SMARTDISPATCH_CONNSTR = "Host=localhost;Username=postgres;Password=CHANGE_ME;Database=smartdispatch"
$env:GUNDI_TEST_SMARTDISPATCH_SCHEMA  = "dbo"

$env:GUNDI_TEST_SMARTONE_CONNSTR = "Host=localhost;Username=postgres;Password=CHANGE_ME;Database=smartone"
$env:GUNDI_TEST_SMARTONE_SCHEMA  = "dbo"

# --- SQL Server readers -----------------------------------------------------

$env:GUNDI_TEST_KAS20_CONNSTR   = "Data Source=localhost;Initial Catalog=kas20;User ID=sa;Password=CHANGE_ME;TrustServerCertificate=True"
$env:GUNDI_TEST_TRBONET_CONNSTR = "Data Source=localhost;Initial Catalog=trbonet;User ID=sa;Password=CHANGE_ME;TrustServerCertificate=True"

# --- Run ---------------------------------------------------------------------

$repoRoot = Split-Path -Parent $PSScriptRoot
$csproj   = Join-Path $repoRoot 'unittests\DataPump.Tests.csproj'

dotnet test $csproj --filter "FullyQualifiedName~DataPump.Tests.Integration"
