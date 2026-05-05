# Building and publishing

How to build the Gundi Radio Service locally, produce release artifacts,
and publish them to the public Google Cloud Storage bucket.

There are two release paths:

- **Velopack** (recommended for new installs) — produces an installer
  (`Setup.exe`) and an auto-update feed. Customers install once and
  receive updates automatically.
- **Legacy zip** (still produced for backward compatibility) — a portable
  zip the customer extracts and registers as a service manually.

This document covers both.

- [Prerequisites](#prerequisites)
- [Versioning](#versioning)
- [Building locally](#building-locally)
- [Producing a release zip](#producing-a-release-zip)
- [Publishing to GCS (legacy zip)](#publishing-to-gcs)
- [Releasing via Velopack auto-update](#releasing-via-velopack-auto-update)
- [End-to-end release](#end-to-end-release)
- [Troubleshooting](#troubleshooting)

---

## Prerequisites

| Tool | Used for | Install |
| --- | --- | --- |
| .NET 8 SDK | Build, test, publish | `winget install Microsoft.DotNet.SDK.8` |
| MSBuild | `publish.proj` orchestration | Comes with Visual Studio Build Tools, or use `dotnet msbuild` |
| Git | Embeds the short SHA in the build's `InformationalVersion` | `winget install Git.Git` |
| Google Cloud SDK (`gsutil`) | GCS upload | <https://cloud.google.com/sdk/docs/install> |
| `vpk` (Velopack CLI) | Auto-update release packaging | `dotnet tool install -g vpk` |
| PowerShell 7+ (recommended) | Running scripts | `winget install Microsoft.PowerShell` |

After installing the Google Cloud SDK, authenticate once:

```powershell
gcloud auth login
```

You also need write access to `gs://radio-connectors/` and the bucket must
be configured for **fine-grained ACLs** (not Uniform Bucket-Level Access)
so per-object public read works.

---

## Versioning

The product version lives in **one place**: [`Version.props`](../Version.props)
at the repo root.

```xml
<Project>
  <PropertyGroup>
    <Version>2.2.0</Version>
  </PropertyGroup>
</Project>
```

Both [`app/DataPump.csproj`](../app/DataPump.csproj) and
[`publish.proj`](../publish.proj) import this file, so:

- Dev and CI builds stamp the version into `AssemblyInformationalVersion`
  (which the runtime User-Agent header reflects via
  `VersionInfo` in [`app/DataPumpLib.cs`](../app/DataPumpLib.cs)).
- The release zip is named `gundi-radio-service-<version>.zip`.
- The GCS upload script picks up the same version automatically.

**To bump the version: edit `Version.props` and commit.** That is the only
required change.

At publish time, `publish.proj` also runs `git rev-parse` and stamps the
short SHA into `SourceRevisionId`, so `InformationalVersion` becomes
`<Version>+<sha>` (e.g., `2.2.0+e3dea9e`). This lets you trace any deployed
binary back to a commit.

---

## Building locally

Restore and build the whole solution:

```powershell
dotnet build
```

Run the unit tests (excludes integration tests, which require a real DB):

```powershell
dotnet test unittests\DataPump.Tests.csproj `
  --filter "FullyQualifiedName!~DataPump.Tests.Integration"
```

Run integration tests against real databases — see
[`unittests/Integration/README.md`](../unittests/Integration/README.md)
for env-var setup and the `*.local.ps1` runner pattern.

---

## Producing a release zip

From the repo root:

```powershell
msbuild publish.proj
```

That runs the default `Package` target, which:

1. Cleans `publish\out\` and any prior zip.
2. Resolves the short git SHA and sets `SourceRevisionId`.
3. Publishes `Configurator\Configurator.csproj` and
   `service\RadioService.csproj` into `publish\out\` using their
   `FolderProfile` publish profiles.
4. Zips the output to `publish\gundi-radio-service-<version>.zip`.

If you don't have `msbuild` on PATH, use the .NET SDK CLI instead:

```powershell
dotnet msbuild publish.proj
```

Both work; pick whichever is convenient.

### Overriding properties

The script accepts standard MSBuild property overrides. Most builds don't
need any of these:

| Property | Default | When to override |
| --- | --- | --- |
| `Version` | from `Version.props` | One-off build with a different stamp; CI testing |
| `PublishConfiguration` | `Release` | Debug build for diagnostics |
| `PublishDir` | `publish\out\` | Custom output location |
| `ZipOutputPath` | `publish\gundi-radio-service-<Version>.zip` | Custom zip path |

Example:

```powershell
msbuild publish.proj /p:Version=2.3.0-rc1 /p:PublishConfiguration=Debug
```

---

## Publishing to GCS

After producing a zip, upload it and grant public read with:

```powershell
.\publish-to-gcs.ps1
```

The script ([`publish-to-gcs.ps1`](../publish-to-gcs.ps1)) reads the
version from `Version.props`, finds the matching zip in `publish\`, uploads
it to `gs://radio-connectors/`, runs `gsutil acl ch -u AllUsers:R` to make
it publicly downloadable, and prints the public URL — for example:

```
Public URL: https://storage.googleapis.com/radio-connectors/gundi-radio-service-2.2.0.zip
```

### Options

```powershell
.\publish-to-gcs.ps1 -Bucket some-other-bucket
.\publish-to-gcs.ps1 -ZipPath C:\path\to\custom.zip
```

### Re-publishing the same version

`gsutil cp` overwrites silently. If you re-run the script for a version
that's already in the bucket, the old object is replaced. If you want
versions to be immutable, bump `Version.props` between releases.

---

## Releasing via Velopack auto-update

Velopack is the auto-update path. A single script does everything: build,
pull prior release state, pack a new release, and publish back to GCS.

```powershell
.\publish-velopack.ps1
```

What the script does, in order:

1. **`dotnet msbuild publish.proj`** to produce `publish/out/`.
2. **`gsutil rsync gs://radio-connectors/velopack/ publish/velopack/`** —
   pulls the prior release set so `vpk pack` can compute delta packages
   against it. First-ever publish on a fresh bucket is a no-op here.
3. **`vpk pack`** — writes `Setup.exe`, a `RELEASES` manifest, a
   `*-full.nupkg` for the new version, and (for releases after the first)
   a `*-delta.nupkg` containing only the diff from the previous full.
4. **`gsutil rsync publish/velopack/ gs://radio-connectors/velopack/`**
   pushes the new release set, then sets per-object public read.

The script prints the resulting URLs at the end:

```
MSI installer (use this): https://storage.googleapis.com/radio-connectors/velopack/GundiRadioService-win.msi
Portable zip (fallback):  https://storage.googleapis.com/radio-connectors/velopack/GundiRadioService-win-Portable.zip
RELEASES manifest:        https://storage.googleapis.com/radio-connectors/velopack/RELEASES
Update feed URL (used by
the running service):     https://storage.googleapis.com/radio-connectors/velopack/
```

### Why MSI, not Setup.exe

Velopack's default `Setup.exe` only does **per-user** installs to
`%LocalAppData%`. That's broken for our case: the Windows service runs as
`LocalSystem`, which doesn't share that user's AppData root, so the
service cannot launch its own binary.

The `--msi True --instLocation PerMachine` flags in `publish-velopack.ps1`
produce an MSI variant that installs **machine-wide** to
`C:\Program Files\Padas\Gundi Radio Service\`, registers HKLM keys, and
prompts for elevation. LocalSystem can read that path, so the service
binPath registered by our `OnAfterInstall` hook works correctly.

Customers should install via the `.msi`, never `Setup.exe`.

### How customers install and update

- **First install**: the customer downloads `GundiRadioService-win.msi`
  and runs it (UAC prompt). The MSI drops the binaries to
  `C:\Program Files\Padas\Gundi Radio Service\` and (via the slice-3
  `OnAfterInstall` hook) registers and starts the Windows service.
- **Subsequent updates**: the running service's `UpdateService` is wired
  to a feed URL (configurable via `Updates:FeedUrl` in `appsettings.json`,
  defaults to the GCS bucket above). Updates are **manual** today — the
  operator clicks "Check for updates" on the Status page; if a newer
  release is available, "Apply update" downloads it and restarts the
  service on the new version. There is no background polling timer. A
  scheduled / automatic check is on the v3.x roadmap; not implemented in
  this PR.

### Delta updates and bandwidth

Delta updates rely on the prior release being present in the feed. The
script's step 2 (rsync from GCS) is what makes this work: `vpk pack`
needs the prior `*-full.nupkg` available locally to diff against. Skip
that step (or publish from a machine with no prior state) and every
release will be a full ~75 MB download for every customer. With deltas,
typical updates are 1–5 MB.

### Code signing (currently skipped, on purpose)

The MSI is built **unsigned** by default. The Gundi Radio Service isn't
distributed to end customers via a public download — a Padas support
team member or technical advisor performs every install on the
customer's box. Those operators can click through the warnings below
without trouble, so the cost of an EV / OV code-signing cert hasn't
justified itself yet.

When the technical advisor runs the unsigned MSI, they will see (in
order):

1. **Browser download warning** ("This file isn't commonly downloaded")
   if the MSI is downloaded via Edge or Chrome from the public bucket.
   One click-through (`...` → Keep, or "Keep" button).
2. **SmartScreen on first run** ("Microsoft Defender SmartScreen
   prevented an unrecognized app from starting"). Click `More info` →
   `Run anyway`.
3. **UAC elevation dialog** showing **Publisher: Unknown** in red text
   instead of a green "Verified publisher: Padas". Click Yes.

After those three clicks the MSI dialogs proceed normally with our
custom branding (icon, license, conclusion).

#### Reducing friction without a cert

Two practices we recommend for the support team's install runbook:

- **Transfer the MSI without Mark-of-the-Web.** Most warnings come from
  the NTFS "downloaded from the internet" stream that browsers attach
  to downloads. `gsutil cp` from the GCS bucket to the customer's box,
  USB transfer, or network share copy avoid this entirely. SmartScreen
  and the "isn't commonly downloaded" warning don't fire when the MSI
  has no MOTW. The recommended transfer for the support team is:

  ```powershell
  gsutil cp gs://radio-connectors/velopack/GundiRadioService-win.msi .
  ```

- **Strip MOTW after browser download.** If the MSI did come from a
  browser, one PowerShell line removes the tag:

  ```powershell
  Unblock-File .\GundiRadioService-win.msi
  ```

  After that, only the UAC "Unknown publisher" remains.

#### When we should revisit signing

Worth reconsidering if any of these become true:

- A customer's IT department uses AppLocker / Software Restriction
  Policies that *block* unsigned executables outright. (Signed in this
  case isn't optional — the MSI literally won't run otherwise.)
- The product moves to a self-install distribution model (e.g., a
  customer downloads the MSI directly without involving the support
  team).
- The number of customer environments grows to a point where
  per-install hand-holding doesn't scale.

The `-SignParams` flag on `publish-velopack.ps1` is wired in for that
day; signing is one cert away from working:

```powershell
.\publish-velopack.ps1 -SignParams "/a /tr http://timestamp.digicert.com /td sha256 /fd sha256"
```

### Known limitations

- **Custom Welcome and Readme installer text** is currently disabled
  in `publish-velopack.ps1` (the `--instWelcome` and `--instReadme`
  flags are intentionally not passed). Velopack's MSI Handlebars
  template uses single-brace placeholders that don't substitute at
  runtime — see [velopack/velopack#877](https://github.com/velopack/velopack/issues/877).
  Re-enable the flags once that ships. License and Conclusion
  screens work and use our content already.

- **Authentication on the embedded web UI is via Windows Negotiate.**
  All endpoints require the requester to be a member of the local
  Administrators group. The operator's browser session passes Windows
  credentials silently when they're already logged in as an admin
  (the typical install flow), so there's no UI prompt for the support
  tech. A non-admin local account or a tool without credentials gets
  401. **Origin** validation rejects any cross-origin request (CSRF /
  cross-site WebSocket hijacking) at the middleware layer, in addition
  to the auth check.

  Operator-runbook implication: the operator must launch their browser
  while logged in as an admin user. If they `Run as administrator`
  Edge/Chrome from a non-admin desktop session, that's enough — the
  browser process's identity is what Negotiate sees.

### Pre-customer-rollout checklist

Beyond the limitations above, the following should be verified before
distributing to a real customer:

- The `assets/installer/LICENSE` content reflects Padas's intended
  license terms. The current Apache 2.0 was placeholder content; the
  MSI displays it as the click-through "I agree" page, so whatever
  is there is what the customer accepts.
- Code signing is not required for the current install model — see
  the "Code signing (currently skipped, on purpose)" section above.
  Revisit only if a customer's IT environment blocks unsigned
  binaries (AppLocker / SRP) or if the distribution model changes.

---

## End-to-end release

The full happy-path workflow (Velopack):

```powershell
# 1. Bump Version.props and commit (so the build's git SHA matches the release)
git add Version.props
git commit -m "Release 2.4.0"
git tag v2.4.0

# 2. Build, pack, and publish in one shot
.\publish-velopack.ps1

# 3. Push the tag
git push origin main --tags
```

For the legacy zip path (still supported for backward compatibility):

```powershell
# 1. Same version bump as above
git add Version.props && git commit -m "Release 2.4.0" && git tag v2.4.0

# 2. Build the zip
msbuild publish.proj

# 3. Upload the zip only
.\publish-to-gcs.ps1

# 4. Push the tag
git push origin main --tags
```

The public URLs printed at step 2/3 are the artifacts to share.

---

## Troubleshooting

**`msbuild` is not recognized**
You're in a plain PowerShell session without VS Build Tools on PATH.
Either open a "Developer PowerShell for VS" shortcut, or use
`dotnet msbuild publish.proj` instead.

**`git rev-parse` not found / SHA not embedded**
`publish.proj` calls git with `ContinueOnError="true"`, so a missing git
or a non-repo build directory produces a zip with no SHA suffix in
`InformationalVersion`. Reinstall git or run from inside the repo.

**`vpk` is not recognized**
The Velopack CLI isn't installed. Run `dotnet tool install -g vpk`. If it
*is* installed but still not found, your PATH may not include
`%USERPROFILE%\.dotnet\tools`.

**Velopack release is full-sized (75+ MB) every time**
The script's step 2 (rsync from GCS) didn't pull prior release state. On
a fresh clone with no `publish/velopack/` directory, this is normal for
the first release; subsequent releases should produce small delta
nupkgs. If they don't, check that `gsutil rsync` actually downloaded
prior `*.nupkg` files into `publish/velopack/` before `vpk pack` ran.

**SmartScreen warning during install**
Expected for our current install model — the MSI is unsigned on
purpose (see "Code signing (currently skipped, on purpose)" above for
the rationale and the workflow recommendations). If you actually need
to sign — for an enterprise customer that blocks unsigned binaries —
pass `-SignParams "..."` to `publish-velopack.ps1`.

**MSI fails to launch with no warning at all**
Usually means the customer's environment blocks unsigned binaries
outright (AppLocker, Software Restriction Policies, or strict
anti-virus). Check with the customer's IT before the visit; either
get a hash-based whitelist exception, or you'll need to start signing
for that customer.

**`gsutil acl ch` fails with "Bucket policy only"**
The bucket has been migrated to Uniform Bucket-Level Access, which
disallows per-object ACLs. Either revert the bucket to fine-grained
access, or grant public read at the bucket level once:

```powershell
gcloud storage buckets add-iam-policy-binding gs://radio-connectors `
  --member=allUsers --role=roles/storage.objectViewer
```

After that, remove the `gsutil acl ch` line from `publish-to-gcs.ps1`
since every object inherits public read.

**`gsutil` not authenticated**
Run `gcloud auth login` once (browser-based). For headless/CI use a
service account: `gcloud auth activate-service-account --key-file=...`.

**Wrong version in the published zip**
Confirm `Version.props` has the value you expect, and that no parent
script is passing `/p:Version=…` on the command line. Command-line
properties override the imported value.
