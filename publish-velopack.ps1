# publish-velopack.ps1
#
# Builds, packs with Velopack, and uploads the release set to GCS so that
# customer installs can auto-update from gs://<bucket>/velopack/.
#
# Workflow:
#   1. dotnet msbuild publish.proj   (produces publish/out/ + the legacy zip)
#   2. gsutil rsync GCS -> publish/velopack/    (so vpk sees prior nupkgs)
#   3. vpk pack                       (writes Setup.exe + RELEASES + nupkgs)
#   4. gsutil rsync publish/velopack/ -> GCS    (publishes the new release)
#   5. gsutil acl ch -r -u AllUsers:R           (per-object public read)
#
# The "rsync GCS -> local" step is what makes delta updates work -- vpk pack
# computes a *-delta.nupkg by diffing the new release against the prior
# *-full.nupkg. Without prior state, every release is full-sized (75 MB+);
# with it, deltas are typically 1-5 MB.
#
# Usage:
#   .\publish-velopack.ps1                  # full release flow
#   .\publish-velopack.ps1 -SkipUpload      # build + pack only, no GCS push
#   .\publish-velopack.ps1 -BumpPatch       # auto-increment patch in Version.props before building
#   .\publish-velopack.ps1 -Force           # overwrite an existing release at the same version (vpk --yes)
#   .\publish-velopack.ps1 -SignParams "/a /tr http://ts /td sha256 /fd sha256"
#
# Prereqs:
#   - dotnet 8+ on PATH
#   - vpk on PATH:    dotnet tool install -g vpk
#   - gsutil on PATH, authenticated:    gcloud auth login
#   - Write access to gs://<bucket>/, fine-grained ACL bucket policy
#     (UBLA buckets reject the per-object ACL grant -- switch to a one-time
#     bucket-level IAM grant if you migrate).

[CmdletBinding()]
param(
    [string]$Bucket = 'radio-connectors',
    [string]$VelopackPrefix = 'velopack',
    [switch]$SkipUpload,
    [switch]$BumpPatch,
    [switch]$Force,
    [string]$SignParams
)

$ErrorActionPreference = 'Stop'

# --- Prereqs ---------------------------------------------------------------

foreach ($tool in @('gsutil', 'vpk', 'dotnet')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "$tool not found on PATH. See docs/building-and-publishing.md for setup."
    }
}

# --- Version (from Version.props, single source of truth) ------------------

$versionPropsPath = Join-Path $PSScriptRoot 'Version.props'
[xml]$xml = Get-Content $versionPropsPath
$version = [string]$xml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Could not read <Version> from $versionPropsPath"
}

# -BumpPatch: increment the patch component of the version and write
# back to Version.props before building. Useful for iterative test
# builds, where Windows Installer's same-version-no-op behavior would
# otherwise cause msiexec /i to skip the deployment after a stale
# uninstall (silently leaving the previous build in place).
if ($BumpPatch) {
    if ($version -notmatch '^(\d+)\.(\d+)\.(\d+)(?<rest>.*)$') {
        throw "Cannot bump patch on non-MAJOR.MINOR.PATCH version '$version' in $versionPropsPath. Edit by hand."
    }
    $major = [int]$Matches[1]
    $minor = [int]$Matches[2]
    $patch = [int]$Matches[3]
    $rest  = [string]$Matches['rest']
    $newVersion = "$major.$minor.$($patch + 1)$rest"

    Write-Host "Bumping version: $version -> $newVersion" -ForegroundColor Cyan
    $xml.Project.PropertyGroup.Version = $newVersion
    $xml.Save($versionPropsPath)
    $version = $newVersion
}

Write-Host "Releasing Gundi Radio Service v$version via Velopack" -ForegroundColor Cyan
Write-Host ""

# --- Paths -----------------------------------------------------------------

$publishOut  = Join-Path $PSScriptRoot 'publish\out'
$velopackOut = Join-Path $PSScriptRoot 'publish\velopack'
$gcsUri      = "gs://$Bucket/$VelopackPrefix/"
$publicBase  = "https://storage.googleapis.com/$Bucket/$VelopackPrefix"

# --- Installer assets ------------------------------------------------------

# Icon selection priority:
#   1. assets/installer/gundi-logo.ico  -- hand-crafted by a designer.
#      Always preferred when present because real graphics tools produce
#      better multi-resolution icons than our PNG-derived fallback.
#   2. assets/installer/gundi.ico       -- auto-generated from
#      gundi_logo.png by scripts/png-to-ico.ps1. Used only when no
#      hand-crafted icon exists. Regenerated on every build so it
#      tracks the source PNG.
#   3. Neither -- installer uses Velopack's default icon, with a warning.
$userIcoPath = Join-Path $PSScriptRoot 'assets\installer\gundi-logo.ico'
$genIcoPath  = Join-Path $PSScriptRoot 'assets\installer\gundi.ico'
$logoPng     = Join-Path $PSScriptRoot 'gundi_logo.png'
$icoPath     = $null

if (Test-Path $userIcoPath) {
    Write-Host "Using hand-crafted icon: gundi-logo.ico" -ForegroundColor Cyan
    $icoPath = $userIcoPath
} elseif (Test-Path $logoPng) {
    Write-Host "Generating gundi.ico from gundi_logo.png ..." -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'scripts\png-to-ico.ps1') -Source $logoPng -Destination $genIcoPath
    if ($LASTEXITCODE -ne 0) { throw "png-to-ico.ps1 failed (exit $LASTEXITCODE)" }
    $icoPath = $genIcoPath
} else {
    Write-Warning "No gundi-logo.ico and no gundi_logo.png; installer will use Velopack's default icon."
}

# Convert any PNG-sourced installer images to BMP, which is what vpk wants
# for --msiBanner / --msiLogo. Same source-of-truth pattern as the ICO:
# the PNG is canonical, the BMP is derived per-build, gitignored.
Add-Type -AssemblyName System.Drawing
function ConvertPngToBmpIfPresent {
    param(
        [string]$PngPath,
        [string]$BmpPath,
        [int]$ExpectedWidth,
        [int]$ExpectedHeight
    )
    if (-not (Test-Path $PngPath)) { return }

    $bmp = [System.Drawing.Bitmap]::FromFile((Resolve-Path $PngPath).Path)
    if ($bmp.Width -ne $ExpectedWidth -or $bmp.Height -ne $ExpectedHeight) {
        Write-Warning ("$(Split-Path -Leaf $PngPath) is $($bmp.Width)x$($bmp.Height); " +
            "vpk requires ${ExpectedWidth}x${ExpectedHeight}. Pack will likely fail. " +
            "Resize the PNG to that exact size.")
    }
    $bmp.Save($BmpPath, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp.Dispose()
    Write-Host "Converted $(Split-Path -Leaf $PngPath) -> $(Split-Path -Leaf $BmpPath)"
}
ConvertPngToBmpIfPresent `
    -PngPath (Join-Path $PSScriptRoot 'assets\installer\msi-banner.png') `
    -BmpPath (Join-Path $PSScriptRoot 'assets\installer\msi-banner.bmp') `
    -ExpectedWidth 493 -ExpectedHeight 58
ConvertPngToBmpIfPresent `
    -PngPath (Join-Path $PSScriptRoot 'assets\installer\msi-logo.png') `
    -BmpPath (Join-Path $PSScriptRoot 'assets\installer\msi-logo.bmp') `
    -ExpectedWidth 493 -ExpectedHeight 312

# --- Step 1: build ---------------------------------------------------------

Write-Host "[1/4] Building (dotnet msbuild publish.proj)..." -ForegroundColor Cyan
& dotnet msbuild (Join-Path $PSScriptRoot 'publish.proj')
if ($LASTEXITCODE -ne 0) { throw "msbuild failed (exit $LASTEXITCODE)" }

if (-not (Test-Path $publishOut)) {
    throw "Expected build output at $publishOut but it does not exist."
}

if (-not (Test-Path $velopackOut)) {
    New-Item -ItemType Directory -Path $velopackOut | Out-Null
}

# --- Step 2: pull prior release state for delta computation ---------------

Write-Host ""
Write-Host "[2/4] Syncing prior release state from $gcsUri ..." -ForegroundColor Cyan
# -m: parallel; -d: delete local files not in source (keeps local in sync
# with what's actually published, so vpk doesn't see stale releases that
# were rolled back). On first publish to a fresh bucket this is a no-op.
gsutil -m rsync -d -r $gcsUri $velopackOut
if ($LASTEXITCODE -ne 0) {
    Write-Warning "gsutil rsync exited $LASTEXITCODE -- proceeding without prior state. First release on this bucket?"
}

# --- Step 3: vpk pack ------------------------------------------------------

Write-Host ""
Write-Host "[3/4] vpk pack ..." -ForegroundColor Cyan
$packArgs = @(
    'pack'
    '--packId',       'GundiRadioService'
    '--packVersion',  $version
    '--packDir',      $publishOut
    '--mainExe',      'RadioService.exe'
    '--packTitle',    'Gundi Radio Service'
    '--packAuthors',  'Padas'
    # Per-machine install via MSI is required for a Windows Service:
    # LocalSystem cannot read the installing operator's %LocalAppData%,
    # so the default per-user install is broken for our scenario. The
    # MSI installs to "Program Files\{packAuthors}\{packTitle}", which
    # LocalSystem can see, and registers HKLM keys.
    '--msi',          'True'
    '--instLocation', 'PerMachine'
    '-o',             $velopackOut
)

# Optional installer-styling assets. Add them only if present so the
# script keeps working when artwork hasn't been supplied yet.
if ($icoPath)                                                          { $packArgs += @('--icon',           $icoPath) }
$bannerPath     = Join-Path $PSScriptRoot 'assets\installer\msi-banner.bmp'
$logoBmpPath    = Join-Path $PSScriptRoot 'assets\installer\msi-logo.bmp'
$welcomePath    = Join-Path $PSScriptRoot 'assets\installer\welcome.txt'
$readmePath     = Join-Path $PSScriptRoot 'assets\installer\readme.txt'
$conclusionPath = Join-Path $PSScriptRoot 'assets\installer\conclusion.txt'

# License file. vpk insists on .txt / .md / .rtf. Priority:
#   1. assets/installer/LICENSE.md   (Markdown)
#   2. assets/installer/LICENSE.rtf  (RTF)
#   3. assets/installer/LICENSE.txt  (plain text, also vpk-friendly)
#   4. assets/installer/LICENSE      (bare, no extension — universal convention.
#                                     Copied to LICENSE.txt at build time so vpk
#                                     accepts it; the .txt is gitignored.)
#   5. assets/installer/license.md   (legacy)
$licensePath = $null
foreach ($candidate in @('LICENSE.md', 'LICENSE.rtf', 'LICENSE.txt', 'license.md')) {
    $path = Join-Path $PSScriptRoot "assets\installer\$candidate"
    if (Test-Path $path) { $licensePath = $path; break }
}
if (-not $licensePath) {
    $bareLicense = Join-Path $PSScriptRoot 'assets\installer\LICENSE'
    if (Test-Path $bareLicense) {
        $licensePath = Join-Path $PSScriptRoot 'assets\installer\LICENSE.txt'
        Copy-Item -Path $bareLicense -Destination $licensePath -Force
        Write-Host "Copied bare LICENSE -> LICENSE.txt for vpk compatibility"
    }
}

if (Test-Path $bannerPath)     { $packArgs += @('--msiBanner',      $bannerPath) }
if (Test-Path $logoBmpPath)    { $packArgs += @('--msiLogo',        $logoBmpPath) }
if ($licensePath)              { $packArgs += @('--instLicense',    $licensePath) }
if (Test-Path $conclusionPath) { $packArgs += @('--instConclusion', $conclusionPath) }

# --instWelcome and --instReadme are deliberately NOT passed to vpk.
# Both flags surface a substitution bug in vpk's MSI Handlebars template
# (MsiLocale_en_US.hbs uses {welcomeMessage} / {readmeMessage} with single
# braces — Handlebars only substitutes {{double}} braces). Result: the
# customer sees the literal "[welcomeMessage]" placeholder text instead
# of our content. The default MSI welcome / readme dialogs render fine
# without the flag.
#
# welcome.txt and readme.txt are still kept in assets/installer/ so
# they're ready to re-enable here when Velopack ships the upstream fix.
# Code signing is deliberately optional. The Gundi Radio Service is not
# downloaded by end customers from a public site -- a Padas support team
# member or technical advisor performs every install on the customer's
# box. Those operators can click through SmartScreen warnings and the
# "Unknown publisher" UAC prompt with no real friction, so paying for a
# code-signing cert hasn't justified itself yet. The -SignParams flag is
# kept for the day a deployment shape changes (broader rollout, an IT
# department with AppLocker, etc.) and we want to start signing.
if ($SignParams) {
    Write-Host "       (signing enabled)"
    $packArgs += @('--signParams', $SignParams)
}

# -Force: when a release at the same version already exists in the local
# mirror (typically because step 2 just rsynced it down from GCS), vpk pack
# prompts interactively for overwrite confirmation. The prompt has a short
# timeout and defaults to "no", which causes the script to fail without
# producing artifacts. Passing --yes accepts the overwrite up front. Use
# this when you mean to re-publish the same version (e.g. fixing a broken
# artifact minutes after the original publish); for ordinary releases,
# -BumpPatch is the right tool.
if ($Force) {
    Write-Host "       (force-overwriting existing release at v$version)"
    $packArgs += '--yes'
}
& vpk @packArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed (exit $LASTEXITCODE)" }

# --- Step 4: upload --------------------------------------------------------

if ($SkipUpload) {
    Write-Host ""
    Write-Host "[4/4] Skipping upload (-SkipUpload). Local artifacts in $velopackOut" -ForegroundColor Yellow
    return
}

Write-Host ""
Write-Host "[4/4] Uploading to $gcsUri ..." -ForegroundColor Cyan
gsutil -m rsync -r $velopackOut $gcsUri
if ($LASTEXITCODE -ne 0) { throw "gsutil rsync upload failed (exit $LASTEXITCODE)" }

# Per-object public read. UBLA buckets reject this; replace with a one-time
# bucket-level IAM grant if you migrate.
Write-Host "       Granting public read on all objects..."
gsutil -m acl ch -r -u AllUsers:R $gcsUri | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Warning "gsutil acl ch failed (exit $LASTEXITCODE). Objects may not be publicly readable; check bucket ACL policy."
}

# --- Done ------------------------------------------------------------------

Write-Host ""
Write-Host "Published Gundi Radio Service v$version" -ForegroundColor Green
Write-Host "  MSI installer (use this): $publicBase/GundiRadioService-win.msi"
Write-Host "  Portable zip (fallback):  $publicBase/GundiRadioService-win-Portable.zip"
Write-Host "  RELEASES manifest:        $publicBase/RELEASES"
Write-Host "  Update feed URL (used by"
Write-Host "  the running service):    $publicBase/"
Write-Host ""
Write-Host "Note: Setup.exe (if produced) is per-user and unsuitable for a Windows Service install."
Write-Host "      Customers must use the .msi, which installs machine-wide to Program Files."
