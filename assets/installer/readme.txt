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
  Configuration:  C:\Program Files (x86)\GundiRadioService\
                    current\appsettings.json
  Logs:           C:\Program Files (x86)\GundiRadioService\
                    current\radioservice.log
  Service:        installed as "Gundi Radio Service" in Windows
                    Service Manager (services.msc)

Updates
  The service checks the public update feed and can apply updates
  in place. Use the "Check for updates" button on the Status page,
  or wait for the periodic background check.

Support
  Use the "Send diagnostic bundle" button on the Status page to
  package logs and sanitized configuration for your support team.
