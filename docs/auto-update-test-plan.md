# Auto-update round-trip test plan

A step-by-step verification that the in-app "Check for updates" / "Apply
update" flow works end-to-end from a real Velopack-installed service.
The code path was implemented in PR #11 but has never been exercised
against a live two-version sequence on the public update feed; the
purpose of this test is to confirm that:

1. A v2.3.0 install can detect a newer published v2.3.1 via the public
   feed.
2. Clicking "Apply update" in the UI succeeds and the operator's
   browser sees the success message rather than a torn connection
   (R1 fix from PR review).
3. After the update, the running service is the new version, the
   process is owned by `NT AUTHORITY\SYSTEM` (proving the SCM-managed
   path, not a detached child process), and `Get-Service` shows
   `Running` (R2 fix from PR review).
4. Cursor state in `state.json` survives across the version swap.

If any check fails, the auto-update path needs further work before
broad customer rollout.

## Prerequisites

- A clean test box (Windows 10/11 VM is fine) with no prior install of
  Gundi Radio Service.
- Reachable database that the configured reader type can talk to.
  Doesn't have to be production data; any working DB the v2.3.0 build
  recognises is enough to produce a `state.json` and a non-zero
  `LastBatchAt`.
- Local clone of the repo, with `publish-velopack.ps1` working and
  `gsutil` authenticated to the bucket.
- Network access from the test box to
  `https://storage.googleapis.com/radio-connectors/velopack/`.

## The plan

### Step 0 — Establish a baseline on the bucket

If the public feed already contains a release at the version you plan
to install (v2.3.0 in this example), skip this step. Otherwise:

```powershell
# bump Version.props to 2.3.0 if it isn't already
.\publish-velopack.ps1   # publishes to gs://radio-connectors/velopack/
```

The bucket should now serve `releases.win.json` listing 2.3.0 as the
latest release.

### Step 1 — Install v2.3.0 on the test box

```powershell
# Download the MSI to the test box. Either:
gsutil cp gs://radio-connectors/velopack/GundiRadioService-win.msi .
# ...or copy via your usual transfer method.

Start-Process msiexec.exe -ArgumentList "/i", "GundiRadioService-win.msi" -Wait
```

After the MSI completes:

```powershell
Get-Service "Gundi Radio Service"               # should be Running
Get-Process RadioService | Select Name, UserName  # owner = NT AUTHORITY\SYSTEM
```

Open <http://localhost:47823/> in a browser. Configure the database and
at least one Gundi destination so the pump actually starts producing
batches. Wait until the Status page shows non-zero "Total batches"
and "Last batch" timestamps. This is what we'll preserve across the
update.

### Step 2 — Publish v2.3.1 to the feed

On your dev machine (NOT the test box):

```powershell
# Bump and publish a real new version.
.\publish-velopack.ps1 -BumpPatch
```

The `-BumpPatch` flag rolls Version.props from 2.3.0 to 2.3.1, the
script then builds + packs + uploads. Confirm by hitting:

```
https://storage.googleapis.com/radio-connectors/velopack/releases.win.json
```

It should now list v2.3.1.

You can leave a small change in 2.3.1 to make it visually obvious the
update happened — e.g., temporarily change a string in
`Pages/Status.razor` to "Pump status (v2.3.1 test)". Reverting after
the test is fine.

### Step 3 — Trigger the update from the running v2.3.0

Back on the test box, with v2.3.0 running:

1. Browse to <http://localhost:47823/>, scroll to the **Updates** card.
2. Note the displayed "Current version" — should read `2.3.0+<sha>`.
3. Click **"Check for updates"**. The button should briefly show
   "Checking…" then change to display **"Update to 2.3.1"**.
4. Click **"Update to 2.3.1"**. The button shows "Updating…".
5. Within a couple seconds the green message **"Update downloaded.
   The service is restarting on the new version."** should appear.
6. Wait ~10–15 seconds for the file swap and SCM restart.

### Step 4 — Verify

The four assertions from the goals at the top of this doc:

```powershell
# 1. Service is registered and Running, not Stopped
Get-Service "Gundi Radio Service"

# 2. Process owner is NT AUTHORITY\SYSTEM (not your operator user)
Get-Process RadioService | Select Name, Id, Path,
    @{N="UserName";E={(Get-Process -Id $_.Id -IncludeUserName).UserName}}

# 3. Deployed binary is the new version
Get-Item "C:\Program Files (x86)\GundiRadioService\current\RadioService.dll" |
    Select Name, LastWriteTime  # should match step-2's publish time

# 4. State preserved
Get-Content "C:\Program Files (x86)\GundiRadioService\current\state.json"
```

Then in the browser:

5. Refresh <http://localhost:47823/>. The Status page should now show
   `2.3.1+<sha>` under "Current version".
6. The "Last batch" timestamp from before the update should still be
   visible (cursor was preserved). Within a few seconds of the
   restart, new batches should start flowing again.

### Pass criteria

All six steps in the verification section show the expected result.
Specifically:

- ✅ Service `Status` is `Running` (not `Stopped`).
- ✅ Process owner is `NT AUTHORITY\SYSTEM`.
- ✅ Binary timestamp matches the v2.3.1 publish.
- ✅ `state.json` carries forward; `LastBatchAt` doesn't reset to null.
- ✅ Browser-side: the "Update downloaded" success message displayed
  before the browser lost the connection.
- ✅ Status page shows the new version after refresh.

If any of these fails, capture:

1. The contents of `C:\Program Files (x86)\GundiRadioService\current\radioservice.log`
   (and any archived rolls under `logs/`).
2. Windows Event Viewer entries for source "Service Control Manager"
   in the last 5 minutes.
3. The exact button state and any error text in the browser at the
   point of failure.

These are the inputs to a debugging session.

## What to do after a successful test

Revert any temporary "v2.3.1 test" string changes in the source. Then
either:

- Leave 2.3.1 published as the canonical second release (and the next
  real release is 2.3.2), or
- If 2.3.1 contained no real changes worth shipping, manually delete
  the v2.3.1 artifacts from the bucket so 2.3.0 remains the latest.
  (Velopack tolerates a "newer" version disappearing — it just won't
  see one for the next CheckForUpdates call.)

## What to do after a failed test

Most likely failure is **the service stops but doesn't restart** —
SCM picks up the graceful exit but doesn't re-launch. R2's fix
explicitly calls `sc start` from `OnAfterUpdateFastCallback`; if that
doesn't work, the alternatives to investigate:

- Velopack's updater process is stripping the `--veloapp-updated`
  argument before respawning, so our hook never fires.
- The hook fires but `sc start` is being called against the wrong
  service name (mismatch with what was registered).
- SCM rejects the start because of permissions on the install
  directory (LocalSystem couldn't read the Program Files path).

The radioservice.log in the install directory is the first place to
look — `OnAfterUpdate` logs "Updated to {version}; signalling SCM to
start the service." If that line is missing, the hook didn't fire and
the issue is upstream of our code.
