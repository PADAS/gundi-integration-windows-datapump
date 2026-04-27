# Building and publishing

How to build the Gundi Radio Service locally, produce a release zip, and
publish that zip to the public Google Cloud Storage bucket.

- [Prerequisites](#prerequisites)
- [Versioning](#versioning)
- [Building locally](#building-locally)
- [Producing a release zip](#producing-a-release-zip)
- [Publishing to GCS](#publishing-to-gcs)
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

## End-to-end release

The full happy-path workflow:

```powershell
# 1. Bump the version in Version.props and commit it
#    (so the SHA stamped into the build is the commit that bumped the version)
git add Version.props
git commit -m "Release 2.3.0"
git tag v2.3.0

# 2. Build the zip
msbuild publish.proj

# 3. Upload
.\publish-to-gcs.ps1

# 4. Push the tag
git push origin main --tags
```

The public URL printed at step 3 is the artifact to share with users.

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
