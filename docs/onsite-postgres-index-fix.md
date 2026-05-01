# Onsite fix: add missing index to speed up the radio data pump

## TL;DR

The Gundi Radio Service is failing on first connect to the customer's
PostgreSQL 10 database with `Timeout during reading attempt`. Cause:
the `dbo.gps_location_data_base` table has no index on
`receive_datetime`, so PostgreSQL has to scan and sort the entire
history table before returning the first 1000 rows. We need you to:

1. Connect to the database on the customer's box.
2. Run a quick `EXPLAIN` to confirm the diagnosis.
3. Create the index.
4. Verify the service starts cleanly.

The whole job is one `CREATE INDEX` statement, plus a couple of
sanity-check queries. Should take under five minutes once you're
connected.

---

## What you need before you arrive

- **Admin/root access** on the Windows box hosting the radio service
  (so you can read the service config and restart the service).
- **The PostgreSQL `postgres` user password** for that box. Get this
  from the customer or from internal records — it is **not** in this
  document.
- **A SQL client.** `psql` is fine if it's installed; otherwise
  download **pgAdmin 4** (free, GUI) from
  <https://www.pgadmin.org/download/> before you go. It works against
  PostgreSQL 10.

---

## Step 1 — Find the database connection details

The radio service config tells us where the database is. On the box,
look for `appsettings.json` next to the running `RadioService.exe`.
The relevant section is `RouteConfiguration`:

```json
"RouteConfiguration": {
    "Hostname": "localhost",
    "DatabaseName": "puc",
    "Username": "postgres",
    "DatabaseSchema": "dbo",
    ...
}
```

For the customer we're seeing:

| Setting   | Value       |
| --------- | ----------- |
| Host      | `localhost` |
| Port      | `5432`      |
| Database  | `puc`       |
| Username  | `postgres`  |
| Schema    | `dbo`       |

Confirm those match what's in their `appsettings.json` before
proceeding — if anything differs, use the value from the file, not
this doc.

---

## Step 2 — Connect to the database

### Option A: `psql` (command line)

From a PowerShell or Command Prompt window on the customer's box:

```powershell
psql -h localhost -U postgres -d puc
```

It will prompt for the password. Once connected, your prompt becomes
`puc=#`. Set the schema once at the start of the session:

```sql
SET search_path TO dbo, public;
```

### Option B: pgAdmin 4 (GUI)

1. Open pgAdmin.
2. Right-click **Servers** → **Register** → **Server…**
3. **General** tab: name it anything (e.g. `customer-puc`).
4. **Connection** tab:
   - Host: `localhost`
   - Port: `5432`
   - Maintenance database: `puc`
   - Username: `postgres`
   - Password: (the password)
5. Click **Save**.
6. Expand the new server → **Databases** → **puc** → **Schemas** →
   **dbo** → right-click **dbo** → **Query Tool**.

You're now in a SQL editor pointed at the right database and schema.

---

## Step 3 — Confirm the diagnosis

Run this query — it asks PostgreSQL to *plan* the query the radio
service runs, but not actually execute the slow part:

```sql
EXPLAIN
SELECT t1.device_alias, t0.guid, t0.receive_datetime
FROM dbo.gps_location_data_base t0
JOIN dbo.device_info t1
  ON t0.device_id = t1.device_id
 AND t0.puc_id = t1.puc_id
 AND t0.system_id = t1.system_id
WHERE t0.receive_datetime > NOW() - INTERVAL '2 days'
ORDER BY t0.receive_datetime ASC
LIMIT 1000;
```

**What you're looking for in the output:**

- If you see lines like `Seq Scan on gps_location_data_base` and
  `Sort` — **the diagnosis is confirmed.** Continue to Step 4.
- If you instead see `Index Scan using <something> on
  gps_location_data_base` — the index already exists and the problem
  is something else. **Stop and contact Chris before changing
  anything.**

You can also list the existing indexes on the table to double-check:

```sql
SELECT indexname, indexdef
FROM pg_indexes
WHERE schemaname = 'dbo'
  AND tablename  = 'gps_location_data_base';
```

If none of the listed indexes mention `receive_datetime`, that's the
missing piece.

---

## Step 4 — Create the index

Run exactly this:

```sql
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_gps_location_data_base_receive_datetime
ON dbo.gps_location_data_base (receive_datetime);
```

Notes:

- **`CONCURRENTLY`** means the table stays writable during index
  creation. The radio dispatch software will keep working normally
  while this runs.
- **It can take a while** on a large table — anywhere from a minute
  to half an hour. Don't cancel it; it's safe to wait.
- If `psql` returns to the `puc=#` prompt without an error, you're
  done. If pgAdmin shows `Query returned successfully` in the
  Messages panel, you're done.

If the customer's table is *very* large and the operation seems
hung after 30 minutes, run this in a *second* session to check
progress:

```sql
SELECT phase, blocks_done, blocks_total
FROM pg_stat_progress_create_index;
```

`blocks_done` should be increasing.

---

## Step 5 — Verify the index works

Re-run the `EXPLAIN` from Step 3. The plan should now contain
**`Index Scan using idx_gps_location_data_base_receive_datetime`**
instead of `Seq Scan` + `Sort`. That's the confirmation.

---

## Step 6 — Restart the radio service

The service has retry/backoff logic so it will eventually pick up the
faster query on its own, but a clean restart skips the wait:

In an **Administrator** PowerShell on the customer's box:

```powershell
Restart-Service RadioService
```

(If they used a different Windows service name, check Services.msc.)

Then watch the log file (typically next to the exe, or wherever NLog
is configured) for a line like:

```
info Created SmartOneDispatchReader. host: localhost, ...
info Starting up
```

…followed by activity that does **not** include
`Database error (attempt N/5)`. If the service runs for two minutes
without timeout warnings, the fix worked.

---

## If something goes wrong

| Symptom | Likely cause | What to do |
| --- | --- | --- |
| `psql: command not found` | psql not installed | Use pgAdmin (Option B) |
| Authentication failure | Wrong password or wrong username | Get fresh credentials; don't keep retrying — PG may lock you out |
| `relation "dbo.gps_location_data_base" does not exist` | Wrong schema | Run `SET search_path TO dbo, public;` first, or check `appsettings.json` for the actual schema name |
| `CREATE INDEX CONCURRENTLY` fails with "cannot run inside a transaction block" in pgAdmin | pgAdmin wraps queries in a transaction by default | In pgAdmin: Query menu → uncheck "Auto-commit", or run the statement via psql |
| Service still throws timeouts after the index | Different table is slow, or network issue | Send the new error log to Chris |

---

## Reference: what the underlying issue is

The radio service's first query against `dbo.gps_location_data_base`
asks for the most recent 1000 rows where `receive_datetime` is within
the last two days, ordered by `receive_datetime`. Without an index on
`receive_datetime`, PostgreSQL must read the whole table from disk and
sort it before it can return the first row. On a multi-million-row
history table, this exceeds the connection's command timeout (raised
to 5 minutes in service version 2.3.0, but still finite).

With the index, PG walks the index in already-sorted order, stops
after 1000 rows, and the query completes in milliseconds regardless
of how big the table grows.

The index is one-time work. It does not need to be recreated after
upgrades, restarts, or backups.
