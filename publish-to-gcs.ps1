# Upload the published zip to gs://<bucket>/ and grant public read.
#
# Usage:
#   .\publish-to-gcs.ps1                          # uploads version from Version.props
#   .\publish-to-gcs.ps1 -ZipPath path\to\foo.zip # uploads a specific file
#   .\publish-to-gcs.ps1 -Bucket other-bucket
#
# Prereqs:
#   - `msbuild publish.proj` has produced publish\gundi-radio-service-<version>.zip
#   - gsutil is on PATH and authenticated (`gcloud auth login`)
#   - Bucket uses fine-grained ACLs (not Uniform Bucket-Level Access);
#     otherwise `gsutil acl ch` fails and you need bucket-level IAM instead.

[CmdletBinding()]
param(
    [string]$Bucket = 'radio-connectors',
    [string]$ZipPath
)

$ErrorActionPreference = 'Stop'

if (-not (Get-Command gsutil -ErrorAction SilentlyContinue)) {
    throw "gsutil not found on PATH. Install the Google Cloud SDK and run 'gcloud auth login'."
}

if (-not $ZipPath) {
    $versionPropsPath = Join-Path $PSScriptRoot 'Version.props'
    [xml]$xml = Get-Content $versionPropsPath
    $version = [string]$xml.Project.PropertyGroup.Version
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Could not read <Version> from $versionPropsPath"
    }
    $ZipPath = Join-Path $PSScriptRoot "publish\gundi-radio-service-$version.zip"
}

if (-not (Test-Path $ZipPath)) {
    throw "Zip not found: $ZipPath. Run 'msbuild publish.proj' first."
}

$fileName  = Split-Path -Leaf $ZipPath
$gcsUri    = "gs://$Bucket/$fileName"
$publicUrl = "https://storage.googleapis.com/$Bucket/$fileName"

Write-Host "Uploading $ZipPath"
Write-Host "      -> $gcsUri"
gsutil cp $ZipPath $gcsUri
if ($LASTEXITCODE -ne 0) { throw "gsutil cp failed (exit $LASTEXITCODE)" }

Write-Host "Granting public read on $gcsUri"
gsutil acl ch -u AllUsers:R $gcsUri
if ($LASTEXITCODE -ne 0) { throw "gsutil acl ch failed (exit $LASTEXITCODE)" }

Write-Host ""
Write-Host "Public URL: $publicUrl"
