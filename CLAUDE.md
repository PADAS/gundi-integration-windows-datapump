# Project conventions

## Branch and PR workflow

**All code changes go through a pull request to `main`. Do not push directly to `main`.**

The repo's branch protection rule says so explicitly: pushes to `main` print

```
remote: Bypassed rule violations for refs/heads/main:
remote: - Changes must be made through a pull request.
```

That bypass is available because the operator has admin rights, but using it routinely defeats the protection's purpose (review, history clarity, the option for a reviewer to gate-keep before something lands). PR #11 (the v3 web UI / Velopack work) was reviewed via PR; everything since has skipped that, which is the gap this rule closes.

### Per-session pattern

1. **Start each chunk of work on a feature branch off the current `main`.**
   ```bash
   git checkout main
   git pull
   git checkout -b feat/<short-descriptive-name>
   ```
2. **Land commits on the feature branch.**
3. **Push the branch and open a PR via `gh pr create`.** Use the GitHub CLI rather than the web UI so the PR title and body are version-controlled in the agent's transcript.
4. **Merge happens via the PR**, not via a direct push to `main`.

### When direct-push is OK

Only with **explicit, in-conversation permission from the operator**. "Just push to main" said in the moment counts; an assumed default doesn't. If you're unsure, ask — the friction of one extra exchange is far smaller than the friction of force-pushing history later to undo an unwanted direct-merge.

### Branch naming

Pick a descriptive, kebab-case name with a type prefix:

- `feat/<description>` — new feature work
- `fix/<description>` — bug fix
- `refactor/<description>` — structural change with no behavior delta
- `docs/<description>` — docs-only change

Keep the description short enough to type but specific enough to disambiguate from other open branches (e.g. `feat/wizard-alias-picker`, not just `feat/picker`).

## Build and verify

After any code change, before commit:

```powershell
dotnet build service\RadioService.csproj -nologo
```

Zero errors required. Existing warnings are tolerated (the codebase has a long-tail nullability warning set we live with).

## Memory

Persistent context — past architectural decisions, project state, deferred items — lives under `~/.claude/projects/<this-project>/memory/`. The index file (`MEMORY.md`) lists what's there. Read those before assuming you're starting fresh on something that may already have been thought through and decided.

Notable indexed items (as of this writing):

- `project_v3_open_items.md` — items deferred from PR #11 still on the roadmap
- `project_branch_pr10.md` — Npgsql 6.x / unit test / integration test context
- `feedback_xml_comments.md` — `--` is illegal in XML doc comments
- `feedback_powershell_ascii.md` — Windows PowerShell 5.1 misreads UTF-8 .ps1 files
- `readers_distinct_schemas.md` — Hytera reader DBs aren't interchangeable
