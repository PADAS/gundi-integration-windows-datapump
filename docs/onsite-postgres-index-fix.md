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

*Implementation note for the curious:* the service at runtime
sends this WHERE as a bound parameter (`> @lower_date`), not the
literal `NOW() - INTERVAL` form above. The diagnostic uses
`NOW() - INTERVAL` on purpose — it's a non-constant expression
that defeats PostgreSQL's plan-time partition pruning, which
forces the planner to evaluate every partition and exposes which
ones lack indexes. A literal cutoff timestamp here would let
the planner prune older partitions, hiding whether they have
indexes at all. Don't "fix" this query to match the service's
runtime form; it's deliberately different.

**What you're looking for in the output:**

- If you see `Seq Scan on gps_location_data_base` plus several lines
  of `Seq Scan on gps_location_data_YYYYMMDD` (daily partition tables
  like `gps_location_data_20230413`, `gps_location_data_20230414`,
  …) — **the table is partitioned.** Confirm the PostgreSQL major
  version with `SELECT version();`. If it's PostgreSQL 10, you need
  **Step 4B** below, not Step 4. PostgreSQL 10 doesn't propagate an
  index from the parent to child partitions; each partition needs
  its own index. PostgreSQL 11 and newer handle this automatically,
  so on those Step 4 alone is sufficient.
- If you see `Seq Scan on gps_location_data_base` and `Sort` with no
  daily partition tables in the output — **the diagnosis is the
  simple non-partitioned case.** Continue to Step 4.
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

## Step 4 — Create the index (non-partitioned table)

Use this step **only** if Step 3 showed `Seq Scan on
gps_location_data_base` without daily partition tables. If you
saw `gps_location_data_YYYYMMDD` lines, **skip to Step 4B**.

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

Then continue to Step 5.

---

## Step 4B — Create indexes on every partition (PostgreSQL 10 with partitioning)

Use this step **only** if Step 3 showed multiple `Seq Scan on
gps_location_data_YYYYMMDD` lines AND `SELECT version();` confirms
PostgreSQL 10. On PG 11+, Step 4 alone is enough.

### Why this is different

PostgreSQL 10 does not propagate an index from a partitioned
parent table to its child partitions. You have to create the same
index on every child partition by hand. PG 11 fixed this; the
single `CREATE INDEX` from Step 4 would have done the right thing
on a newer database.

### Generate the per-partition statements

Run this **SELECT** in your SQL client. It produces one row per
child partition; each row is a complete `CREATE INDEX
CONCURRENTLY IF NOT EXISTS …` statement you'll execute in the
next sub-step.

```sql
SELECT
    'CREATE INDEX CONCURRENTLY IF NOT EXISTS '
    || 'idx_' || c.relname || '_receive_datetime '
    || 'ON ' || n.nspname || '.' || c.relname
    || ' (receive_datetime);' AS create_stmt
FROM pg_inherits i
JOIN pg_class     c ON c.oid = i.inhrelid
JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE i.inhparent = 'dbo.gps_location_data_base'::regclass
ORDER BY c.relname;
```

You should get a list of statements that looks roughly like:

```
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_gps_location_data_20230413_receive_datetime ON dbo.gps_location_data_20230413 (receive_datetime);
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_gps_location_data_20230414_receive_datetime ON dbo.gps_location_data_20230414 (receive_datetime);
...
```

Expect anywhere from a handful to several hundred rows depending
on how long the customer's database has been running.

### Run them

Copy the output and execute the statements. **Important PG 10
quirk:** `CREATE INDEX CONCURRENTLY` cannot run inside a
transaction block, so do **not** wrap them in `BEGIN; … COMMIT;`.
Just paste them into psql (or run them one-at-a-time in pgAdmin
with auto-commit on; see the "If something goes wrong" table at
the end of this doc for pgAdmin's auto-commit toggle).

Each statement takes a few seconds. The whole batch is safe to
run while the database is in use.

### `IF NOT EXISTS` makes the script re-runnable

The generated SELECT + apply pattern is idempotent — partitions
that already have the index get skipped. If new daily partitions
appear later (PG 10 doesn't auto-index those either), running the
SELECT again and applying any new rows will catch them.

### Heads-up about future partitions

This is a workaround for an existing index gap, not a permanent
fix. Going forward, every new `gps_location_data_YYYYMMDD`
partition the customer's database creates will start without an
index. How that gets handled depends on how new partitions are
provisioned on this box:

- If created by a **partition manager** like `pg_partman`,
  configure the manager's partition template to include the
  index. Tell Chris which manager is in use; we can prepare the
  template change.
- If created **manually** by the customer or by a vendor script,
  the new partition needs the index added manually too.
- If you don't know how the partitions are getting created — ask
  the customer; the answer affects whether this fix sticks long-
  term.

Then continue to Step 5.

---

## Step 5 — Verify the index works

Re-run the `EXPLAIN` from Step 3. The plan should now contain
`Index Scan using …` lines instead of `Seq Scan` lines on the
gps tables. Specifically:

- **Non-partitioned (after Step 4):** look for
  `Index Scan using idx_gps_location_data_base_receive_datetime`
  in place of `Seq Scan on gps_location_data_base` + `Sort`.
- **Partitioned (after Step 4B):** every
  `gps_location_data_YYYYMMDD` row in the plan should now read
  `Index Scan using idx_gps_location_data_YYYYMMDD_receive_datetime`
  instead of `Seq Scan`. The total cost in the plan's top line
  should drop by roughly two orders of magnitude.

If you still see `Seq Scan` on any partition after Step 4B, that
specific partition didn't get the index (most likely because the
SELECT in Step 4B missed it — re-run that SELECT to see if the
list of generated statements covers every partition mentioned in
the EXPLAIN, and apply any you didn't run the first time).

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

The index itself is one-time work. It does not need to be recreated
after upgrades, restarts, or backups.

**Exception — partitioned databases on PostgreSQL 10.** When the
table is partitioned (Step 4B applied), the index is per-partition,
and new partitions created by the customer's database going
forward will not inherit one. The fix is still durable for
existing partitions, but every new daily partition needs its own
index. PostgreSQL 11+ fixes this; an upgrade is the long-term
answer. Re-running the Step 4B SELECT periodically is the
short-term workaround — see the "Heads-up about future
partitions" section under Step 4B.
