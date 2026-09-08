# shapez 2 Modding Docs

Unofficial, community documentation for modding **shapez 2** with the
[ShapezShifter](https://github.com/tobspr-games/shapez2-shifter) API.

Two halves:

- **Guide** (`docs/`) — hand-written, task-first pages: how to add a building, read
  what a machine is doing, draw over the world, hook behaviour that has no builder API.
- **API reference** (`api/`) — generated with [DocFX](https://dotnet.github.io/docfx/)
  from the shipped game assemblies. Signatures, inheritance, and implementors for
  ~4,700 types. The game ships no XML doc comments, so there is no prose — that is what
  the guide is for.

## Scope

Setup, `manifest.json`, dependencies, and translations are already covered well by the
[wiki](https://shapez2.wiki.gg/wiki/Mod_Development); ShapezShifter's content-authoring
builders are covered by the
[official Notion docs](https://tobspr-games.notion.site/shapez2-modding-documentation).
There is also an [unofficial docs site](https://shapez2.raphdf201.net/).

This repo focuses on **working with the game that is already there**, which none of
those cover in depth.

## Building locally

Requires the .NET 8 SDK, [DocFX](https://dotnet.github.io/docfx/), and a shapez 2
install.

```bash
dotnet tool install -g docfx
```

Set the modding environment variables (on Windows the game does it for you with
`"shapez 2.exe" --set-modding-env-vars`):

| Variable | Points at |
| --- | --- |
| `SPZ2_PATH` | the game's managed assemblies directory |
| `SPZ2_SHIFTER` | `ShapezShifter.dll` (the file, not its folder) |

Then:

```bash
bash scripts/write-local-config.sh          # substitute your paths
docfx metadata docfx.metadata.local.json    # assemblies -> api/*.yml  (~4 min)
docfx build docfx.ci.json                   # -> _site/
docfx serve _site                           # http://localhost:8080
```

Guide-only changes need no game install — `docfx build docfx.ci.json` works as long as
`api/*.yml` already exists.

## Repository layout

```text
docs/                    the guide (markdown)
  howto/                 task-first recipes
api/index.md             API reference landing page (committed)
api/*.yml                generated metadata (NOT committed)
templates/spz2/          custom DocFX theme
docfx.ci.json            build config — machine-independent, used by CI
docfx.metadata.json      metadata config template (paths substituted at build time)
filterConfig.yml         keeps engine/runtime types out of the reference
scripts/                 local build helpers
.github/workflows/       Pages pipelines
```

## Why the API metadata is not in this repo

`docfx metadata` needs the game's assemblies as input. Those are proprietary, ship only
with the paid game, and cannot be redistributed — so a GitHub-hosted runner has no way
to obtain them:

- ShapezShifter publishes **no compiled binaries**, and building it from source needs
  the game assemblies too.
- tobspr publishes **no reference-assembly package**.
- Paid Steam content cannot be downloaded anonymously; automating a real account's
  login in CI risks the account and is against Valve's terms.

The generated metadata is also large — ~535 MB of YAML, mostly DocFX reference blocks —
so committing it is not an option either. It compresses to ~24 MB.

Hence two pipelines, and you should pick one:

### Option A — release asset (default, `pages.yml`)

Runs on `ubuntu-latest`. Downloads `api-metadata.tar.gz` from a rolling
`api-metadata` release, unpacks it, builds, deploys. Nothing large in git history.

One manual step after a game update:

```bash
bash scripts/refresh-api-metadata.sh
gh release upload api-metadata api-metadata.tar.gz --clobber
```

First time:

```bash
gh release create api-metadata api-metadata.tar.gz \
  --title "API metadata" --notes "Generated DocFX metadata"
```

No `gh` CLI? Upload the file to the `api-metadata` release through the browser.

### Option B — self-hosted runner (`pages-selfhosted.yml`)

Fully automatic, no bundle and no release asset: the pipeline generates the metadata
itself. It just has to run on a machine that owns the game.

1. Register a self-hosted runner (repo *Settings → Actions → Runners*) with the label
   `shapez2`, on a machine with shapez 2 installed and `SPZ2_PATH` / `SPZ2_SHIFTER` set
   in the runner's environment.
2. In `pages-selfhosted.yml`, change `on: workflow_dispatch:` to
   `on: push: branches: [main]`.
3. Disable `pages.yml` so the two pipelines do not race for the Pages deployment.

Option B is the better end state if you are happy running a runner; Option A needs no
infrastructure but costs one upload per game update.

## Enabling GitHub Pages

Repo *Settings → Pages → Build and deployment → Source: **GitHub Actions***. The
workflows use `upload-pages-artifact` / `deploy-pages` and need no `gh-pages` branch.

## Contributing

The guide's rule is that everything stated as fact was read out of the shipped
assemblies. Where a page documents an entry point but the territory beyond it is
untested, it says so in a **Note** — please keep that habit rather than removing the
caveats.

Verify a claim before writing it:

```bash
grep -rn "SomeType" /path/to/decompiled/tree
```

See [Exploring the Assemblies](docs/exploring-assemblies.md) for the decompilation
workflow that produced most of this guide.

> **Note**
> Keep decompiled game code out of this repository. The API reference publishes
> signatures only; decompiled method bodies are a different matter, and the local
> decompiled tree should stay local.

## Licence

The documentation text in `docs/` is contributed by the community. shapez 2 and its
assemblies are © tobspr Games; this project is unofficial and not affiliated with them.
