# Troubleshooting

First stop for "something isn't right." If none of these match
your situation, the **Support** card on the Status page has a
**Download diagnostic bundle** button — send the resulting zip
to your Padas contact.

## The desktop shortcut doesn't open anything

Open a browser yourself and type
`http://localhost:47823` into the address bar.

- **Page loads:** the shortcut file's URL is out of date.
  Right-click the shortcut → Properties → check the URL. Should
  read `http://localhost:47823/`. If not, delete the shortcut
  and reinstall the service (or wait for the next update — the
  service rewrites the shortcut on every update).
- **Page doesn't load:** the service isn't running, or it's
  running on a different port. Check the Service Manager:
  press Win + R, type `services.msc`, look for "Gundi Radio
  Service." It should be Running. If it's Stopped, right-click
  → Start. If starting fails, the Event Viewer's System log
  has the SCM error.

## Status page says "Not configured"

The pump's idle-on-error state. Click the **Set up the
service** button to open the wizard, or head to the
Configuration page if you've used it before.

## Status page shows the pump as Running but no batches appear

A few possibilities. In rough order of likelihood:

**The database has no recent data.** If "Last batch" shows a
very old timestamp or "(never)," and no errors are logged, the
pump is healthy but the source database hasn't received any
new records the pump is interested in. Check the dispatch
console: are radios actively reporting?

**The database is slow on the first query.** The pump's
initial poll fetches the most recent two days of records. On
a large unindexed table, that can take longer than the
configured timeout (default 5 minutes). The log line is
`Database error (attempt N/5)` with a `Timeout` exception.
The fix is on the database side — usually adding an index on
the timestamp column the pump filters by. Download the
diagnostic bundle (Status page → Support card) and send it to
your Padas contact; a support tech can confirm and provide
the exact SQL for your database.

**Group filters exclude everything.** If "Send all
observations" is off and your alias list is wrong (a GUID
typo, or filters for a group that has no records), the pump
will run silently with no posts. Check the Configuration
page → destinations → group aliases.

**Gundi API key is rejected.** The log line is
`401 Unauthorized` or similar from `GundiV2DataWriter`. The
Configuration page's **Test API key** button surfaces this
explicitly. Re-paste the key from your Gundi project page.

## Update flow stalled

If you clicked **Update to X.Y.Z** and the page didn't
recover after a couple of minutes:

1. **Refresh the browser tab.** The reconnection modal usually
   shows; click its Refresh button. If the modal doesn't
   appear, press F5 manually.
2. **Check the service is running.** Open Service Manager
   (`services.msc`) and look for "Gundi Radio Service."
   Should be Running. If Stopped, right-click → Start.
3. **Look at `apply.log`.** Lives at
   `C:\Program Files (x86)\GundiRadioService\apply.log`. The
   last line should read "Package version X.Y.Z applied
   successfully." If it shows a different error or the file
   is older than your click, the update step itself failed.
   Send the file to your Padas contact.

## "Windows protected your PC" on install

Expected. The installer isn't yet code-signed. Click **More
info** → **Run anyway**. If you're worried about the
authenticity of the download, the URL
`https://storage.googleapis.com/radio-connectors/velopack/GundiRadioService-win.msi`
is HTTPS-served from Google Cloud Storage and Padas publishes
exclusively to that bucket.

## I want to remove the service

Use the standard Windows path: **Settings** → **Apps** →
**Installed apps** → find "GundiRadioService" → **Uninstall**.
This stops the service, removes the binaries, removes the
desktop shortcut, and unregisters the service from SCM.

What it does **not** remove:

- `C:\ProgramData\GundiRadioService\` — operator config and
  cursor state. Left intact so a reinstall picks up where you
  left off.
- The downloaded MSI in your Downloads folder (delete
  manually if you want).

If you want a fully clean wipe, delete
`C:\ProgramData\GundiRadioService\` by hand after uninstall.

## Something else

The Status page's **Support** card has a **Download
diagnostic bundle** button. It builds a zip containing:

- The current log file.
- Archived log rolls (older history).
- A redacted copy of the operator config (no passwords or
  API keys).
- The cursor state file.
- A metadata file with version, machine name, OS, and a snapshot
  of pump status at bundle time.

Email it to your Padas support contact with a brief description
of what you observed. The bundle has everything we need to
diagnose remotely.
