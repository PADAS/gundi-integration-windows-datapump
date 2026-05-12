# Get started

Install takes about three minutes once you're at the box.

## What you need

- A Windows 10 or Windows 11 PC (or Windows Server 2019+).
- **Administrator rights** on that PC — the installer registers a
  Windows Service, which requires elevation.
- **Network access** from the PC to
  `https://*.cdip-api*.pamdas.org` (where Gundi listens).
- The connection details for the customer's radio dispatch
  database (host, port, database name, username, password) and
  schema if it's PostgreSQL-style.
- A **Gundi API key** for each destination you'll forward to.

## Step 1 — Download

Open this link in a browser on the target PC:

[**Download GundiRadioService-win.msi**](https://storage.googleapis.com/radio-connectors/velopack/GundiRadioService-win.msi)

The URL always serves the most recent published version. Save the
file somewhere convenient — Downloads is fine.

!!! note "Windows SmartScreen warning"
    The installer isn't code-signed yet, so the first time you
    run it Windows will show a blue *"Windows protected your
    PC"* dialog. Click **More info** → **Run anyway**. If you
    download it from this URL you can be confident the file is
    the one Padas published.

## Step 2 — Install

Double-click the downloaded `.msi`. Accept the elevation prompt
when Windows asks. The installer is silent past that point — no
options to pick, no destination folder to choose. About 15
seconds later you're done.

What happened in the background:

- Files were copied to `C:\Program Files (x86)\GundiRadioService\`.
- A Windows Service named "Gundi Radio Service" was registered to
  auto-start on boot.
- A desktop shortcut named **Gundi Radio Service** was placed on
  the All Users desktop. It opens the local web UI in your
  default browser.

## Step 3 — First-run wizard

Double-click the desktop shortcut. Your browser opens at the
local UI, and because nothing's configured yet you'll see the
**Welcome — let's get the service running** banner.

Click **Set up the service** to start the wizard. It walks
through four short steps:

1. **Welcome.** A summary of what's about to happen.
2. **Source database.** Pick the reader type (Smart Dispatch
   Plus, Smart One Dispatch, Kenwood KAS20, or TRBOnet) and
   fill in the host, database name, username, and password.
   The **Test connection** button is right there — use it
   before moving on.
3. **Gundi destinations.** Add one or more Gundi or
   EarthRanger destinations. Each one takes a name, the URL,
   and an API key. **Test API key** verifies the credentials
   end-to-end. If you want to forward only certain radio
   groups, leave **"Send all observations"** off and pick the
   groups from the database via the **"Pick from database…"**
   button.
4. **Review & save.** Confirm the summary, click **Finish**.
   (The button momentarily reads "Saving…" while the configuration
   is written and the pump is reloading.)

<!-- Replace with a screen recording of walking through the
     wizard. Suggested file: videos/first-run-wizard.mp4 (or .gif
     if under ~20 seconds; the four steps fit comfortably). Show
     a successful Test connection and Test API key. -->
<!-- ![First-run wizard walkthrough placeholder](videos/first-run-wizard.gif){ width="800" } -->

The service starts pumping data immediately after Finish. You can
close the browser; the service keeps running.

## Step 4 — Verify

Reopen the desktop shortcut. The Status page should show:

- **State**: Running
- **Started**: a few seconds ago
- **Last batch**: a record set with a recent timestamp
- **Totals**: rising over time as more batches go through

If the State is **Running** but no batches appear within a few
minutes, head to **[Troubleshooting](troubleshooting.md)**.

## What's next

- **[Using the service](using-the-service.md)** — day-to-day:
  pausing, checking status, reading logs, downloading a
  diagnostic bundle.
- **[Updating](updating.md)** — how the in-app update flow
  works and what to expect when you click it.
