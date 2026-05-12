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

- If you see `Seq Scan on gps_location_data_base` plus several
  lines of `Seq Scan on gps_location_data_YYYYMMDD` (daily
  partition tables like `gps_location_data_20230413`,
  `gps_location_data_20230414`, …) — **the table is partitioned.**
  Go to **Step 4B** below, not Step 4. Each partition needs its
  own index on `receive_datetime`; the one we'd create on the
  parent table doesn't always reach the children. (See the note
  at the top of Step 4B for the reason.)
- If you see `Seq Scan on gps_location_data_base` and `Sort` with
  no daily partition tables in the output — **the diagnosis is
  the simple non-partitioned case.** Continue to Step 4.
- If you instead see `Index Scan using <something> on
  gps_location_data_base` — the index already exists and the
  problem is something else. **Stop and contact Chris before
  changing anything.**

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
hung after 30 minutes, you can check progress.

On **PostgreSQL 12 and newer** there's a dedicated progress view:

```sql
SELECT phase, blocks_done, blocks_total
FROM pg_stat_progress_create_index;
```

`blocks_done` should be increasing.

On **PostgreSQL 10 or 11** that view doesn't exist; check that the
backend is still alive and not stuck on a lock instead:

```sql
SELECT pid, state, wait_event_type, wait_event,
       LEFT(query, 80) AS query
FROM pg_stat_activity
WHERE query ILIKE '%CREATE INDEX%CONCURRENTLY%'
  AND state <> 'idle';
```

You should see one row with `state = active`. If `wait_event` is
something like `transactionid` or `relation` for several minutes,
another long transaction is blocking the index creation — usually
that resolves on its own. If it doesn't, send the row output to
Chris.

Then continue to Step 5.

---

## Step 4B — Create indexes on every partition (partitioned tables)

Use this step if Step 3 showed multiple `Seq Scan on
gps_location_data_YYYYMMDD` lines. This is correct for every
partitioned setup we've seen — see the note below for the
PostgreSQL-version nuance.

### Why this is different

Whether the parent's index reaches the child partitions depends
on *how* the table is partitioned, and the EXPLAIN output alone
doesn't tell us which kind we're looking at:

- **Inheritance-based partitioning** (`CREATE TABLE … INHERITS
  (parent)`): the parent's index **never** propagates, on any
  PostgreSQL version. You always need a per-partition index. This
  is the older partitioning style and what we've actually seen
  on Padas customer boxes.
- **Declarative partitioning** (`CREATE TABLE … PARTITION OF
  parent`, introduced in PG 10): the parent's index propagates
  to children automatically — but **only on PostgreSQL 11 and
  newer**. PG 10's declarative partitioning shipped without that
  feature.

The script in this step uses `pg_inherits`, which enumerates
child tables regardless of partitioning style, and creates the
index `IF NOT EXISTS` on each. So running it is correct for
inheritance partitioning on any version, correct for declarative
on PG 10, and harmless (no-op per partition) for declarative on
PG 11+ where the children already have an inherited index. Safe
to run any time Step 3 turned up partition lines.

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

Copy the output and execute the statements. **Important:**
`CREATE INDEX CONCURRENTLY` cannot run inside a transaction
block on any PostgreSQL version, so do **not** wrap them in
`BEGIN; … COMMIT;`. The reliable way is psql — paste the
statements in and each runs on its own. If you're in pgAdmin
instead, see the "If something goes wrong" table at the end of
this doc for the Auto-commit toggle pgAdmin needs.

Each statement takes a few seconds. The whole batch is safe to
run while the database is in use.

### `IF NOT EXISTS` makes the script re-runnable

The generated SELECT + apply pattern is idempotent — partitions
that already have the index get skipped. If new daily partitions
appear later without their own index, re-running the SELECT and
applying any new rows will catch them.

### Heads-up about future partitions

Whether new daily partitions need this treatment depends on the
partitioning style and PostgreSQL version (see "Why this is
different" at the top of Step 4B). For inheritance-based
partitioning, every new partition will start without an index
regardless of PostgreSQL version. For declarative partitioning,
PG 11+ takes care of it automatically and there's nothing to do
going forward.

If new partitions will need indexes added, how it gets handled
depends on how new partitions are provisioned on this box:

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

**Exception — partitioned databases.** When the table is
partitioned (Step 4B applied), the index is per-partition.
Whether new daily partitions created by the customer's database
will inherit one depends on the partitioning style and the
PostgreSQL version:

- **Inheritance-based partitioning** (any PG version): new
  partitions don't inherit. Every new daily table needs its own
  index. Re-run the Step 4B SELECT periodically — or, better,
  fix the source of new partitions to add the index inline.
- **Declarative partitioning on PG 10**: same as above — PG 10
  shipped declarative partitioning without parent-to-child
  index propagation.
- **Declarative partitioning on PG 11+**: new partitions inherit
  the parent's index automatically. Nothing to do going
  forward.

If unsure which kind, the safe move is to re-run Step 4B's
SELECT every so often; it's idempotent and will catch any
unindexed partitions.
