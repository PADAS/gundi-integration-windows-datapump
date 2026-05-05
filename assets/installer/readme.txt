Gundi Radio Service

Reads radio location data from a local SQL Server or PostgreSQL
database (Kenwood KAS-20, Hytera Smart Dispatch Plus, Hytera
Smart One Dispatch, or TRBOnet Plus) and forwards it to a Gundi
or EarthRanger destination via HTTPS.

System requirements
  - Windows 10, Windows 11, or Windows Server 2016 or later
  - Network access to the dispatch database (read-only is fine)
  - Outbound HTTPS access to the Gundi/EarthRanger API

After installation
  1. Open http://localhost:8080/ in your browser.
  2. On the Configuration page, fill in the database connection
     details and use "Test connection" to verify them.
  3. Add at least one Gundi destination with its API key, then
     "Test API key" to confirm.
  4. Click Save -- the service starts pumping immediately.

File locations
  The installer drops the application files in a versioned subfolder
  under the install root. The current version is always reachable via
  the "current" subfolder. By default this is roughly:

    <install root>\current\appsettings.json   (configuration)
    <install root>\current\radioservice.log   (NLog output)
    <install root>\current\state.json         (cursor)

  The exact install root depends on Windows configuration; consult
  Add/Remove Programs to find it, or check the service's binPath:

    sc qc "Gundi Radio Service"

  The Windows service is registered as "Gundi Radio Service" and
  visible in services.msc.

Updates
  Open the Status page in your browser (http://localhost:8080/) and
  click "Check for updates". If a newer version is available, click
  "Apply update" to download and install it. The service restarts
  automatically on the new version. Updates are manual; there is
  no automatic background check.

Support
  Use the "Download diagnostic bundle" button on the Status page to
  package logs and sanitized configuration into a zip you can email
  to your support team.
