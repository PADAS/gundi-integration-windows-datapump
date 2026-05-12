# docs-site — public-facing documentation site

This folder is the source for the Material for MkDocs site that
GitHub Pages publishes at
<https://padas.github.io/gundi-integration-windows-datapump/>.

Everything in here is Markdown + YAML; the heavy lifting is done
by `mkdocs-material` (pinned in `requirements.txt`) and a
GitHub Action (`.github/workflows/deploy-docs.yml`).

## Layout

```
docs-site/
├── mkdocs.yml          # site config (theme, nav, extensions)
├── requirements.txt    # mkdocs-material version pin
├── README.md           # this file
└── docs/
    ├── index.md
    ├── getting-started.md
    ├── using-the-service.md
    ├── updating.md
    ├── troubleshooting.md
    ├── images/         # screenshots (PNG preferred)
    └── videos/         # short GIFs and MP4 clips
```

The `docs/images/` and `docs/videos/` folders don't exist yet —
create them as you start dropping in media.

## Preview locally before pushing

You need Python 3.9+ on PATH. Then, from this folder:

```powershell
# One-time:
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt

# Every time you want to preview:
mkdocs serve
```

`mkdocs serve` watches the filesystem and live-reloads at
<http://localhost:8000> as you edit. Stop with Ctrl+C.

If port 8000 is in use:

```powershell
mkdocs serve --dev-addr 127.0.0.1:8765
```

## Publishing

You don't run a publish command yourself. The
`.github/workflows/deploy-docs.yml` action triggers on every
push to `main` that touches `docs-site/**`, builds the site, and
pushes the rendered HTML to the `gh-pages` branch. GitHub Pages
serves from there.

To force a redeploy without a content change (e.g., after a
theme version bump), use the **Actions** tab → **Deploy docs to
GitHub Pages** → **Run workflow**.

## One-time repo setup

The first time this site is deployed, the repo's
**Settings** → **Pages** needs to be told to publish from the
`gh-pages` branch:

1. **Source**: Deploy from a branch
2. **Branch**: `gh-pages` / `/ (root)`
3. Save.

After the first deploy completes, the published URL appears at
the top of the Pages settings page.

## Adding screenshots

1. Capture with [ShareX](https://getsharex.com) (free) — region
   capture, annotation, blur for any sensitive bits.
2. Save as **PNG** under `docs/images/`. Lowercase, hyphenated
   filenames: `status-page-healthy.png`, `wizard-step-2.png`.
3. Reference from the markdown with Material's image syntax:

   ```markdown
   ![Status page](images/status-page-healthy.png){ width="800" }
   ```

   The `{ width="800" }` keeps wide screenshots from blowing out
   the column.

## Adding animations or short videos

For UI flow demos under ~20 seconds, **animated GIF** is the
simplest path. Capture with
[ScreenToGif](https://www.screentogif.com) (free). Save under
`docs/videos/`, reference with the same image syntax:

```markdown
![Wizard walkthrough](videos/first-run-wizard.gif){ width="800" }
```

For longer clips (more than ~20 seconds), use **MP4**. Material
for MkDocs supports `<video>` directly:

```html
<video controls width="800">
  <source src="../videos/update-flow.mp4" type="video/mp4">
</video>
```

For *long* videos (more than ~1 minute), upload to **YouTube as
unlisted** and embed instead. GitHub Pages has bandwidth limits
that long MP4s can blow through; YouTube handles delivery for
free.

## Style notes

- Headings: H1 once at the top of each page (the title). H2 for
  sections, H3 for sub-sections. Material's sidebar uses these
  for the per-page outline.
- Admonition boxes (`!!! note "Title"`, `!!! warning`, `!!! tip`)
  for callouts. Don't use them for every paragraph; they lose
  effect when overused.
- Code blocks: language hint (` ```powershell ` / ` ```sql `) so
  the copy-button knows what's inside and syntax highlighting
  works.
- Cross-page links: relative, with the `.md` extension —
  `[Updating](updating.md)`. Material rewrites these to the
  final URL at build time.

## Versioning (deferred)

If this site grows enough to need per-release docs (one URL per
service version), the standard tool is `mike` (Material for
MkDocs's versioning extension). Not set up yet — the assumption
is the site documents the current shipping version, full stop.
