# Publish to the Steam Workshop

**Problem.** Your mod works locally. Now it has to reach players.

**Solution.** `steamcmd` plus a `.vdf` manifest. The sample mods ship a working script;
this page is that script explained, including the one line you **must** change.

> [!WARNING]
> The sample `SteamPublish.sh` contains `steamcmd +login lorenzo_tobspr` — a tobspr
> developer account. Copy it unchanged and the upload fails on login. Replace it with
> your own Steam account name before running anything.

## What you need

- **`steamcmd`** on your `PATH` ([Valve's download](https://developer.valvesoftware.com/wiki/SteamCMD))
- A Steam account that has **accepted the Workshop legal agreement** — do this once in a
  browser, or your first upload is rejected
- **bash with `envsubst` and `cygpath`** on Windows — Git Bash provides both
- A **`preview.png`** — the Workshop requires a preview image

## The layout

```text
MyMod/
├── MyMod.csproj
└── Steam/
    ├── base.vdf            the item manifest
    ├── preview.png         Workshop thumbnail
    └── SteamPublish.sh     the upload script
```

## base.vdf

```text
"workshopitem"
{
    "appid" "2162800"
    "publishedfileid" "0"
    "contentfolder" "${CONTENT_PATH}"
    "previewfile" "${PREVIEW_IMG}"
    "visibility" "0"
    "title" "My Mod"
    "description" "What the mod does"
}
```

| Key | Meaning |
| --- | --- |
| `appid` | always `2162800` — shapez 2 |
| `publishedfileid` | `0` creates a **new** item; a real id **updates** that item |
| `contentfolder` | absolute path to your built mod folder — filled in by the script |
| `previewfile` | absolute path to the thumbnail — filled in by the script |
| `visibility` | `0` public, `1` friends only, `2` private, `3` unlisted |

Start with `visibility "2"` while testing. A half-broken public item collects
one-star ratings faster than you can fix it.

## How the script works

```bash
CONTENT_PATH=$1                          # passed in by the csproj target
CURRENT_DIR=$(cygpath -w "$PWD")         # POSIX path → Windows path
PREVIEW_IMG=$CURRENT_DIR\\Steam\\preview.png

export CONTENT_PATH
export PREVIEW_IMG
envsubst < Steam\\base.vdf > Steam\\base.tmp.vdf   # substitute the two paths

steamcmd +login YOUR_ACCOUNT +workshop_build_item "$TMP_VDF" +quit

# read the id Steam assigned and write it back into base.vdf
FILE_ID=$(grep '"publishedfileid"' Steam\\base.tmp.vdf | sed 's/.*"\([0-9]\+\)".*/\1/')
sed -i 's/\("publishedfileid"[ \t]*"\)[0-9]\+"/\1'"$FILE_ID"'"/' Steam\\base.vdf
```

The clever part is the last step: after a successful first upload, Steam writes the new
`publishedfileid` into the temp manifest, and the script copies it back into
`base.vdf`. Every later run therefore **updates** the same Workshop item instead of
creating duplicates.

**Commit `base.vdf` after your first publish.** Losing that id means losing the ability
to update your own item, and there is no way to reclaim it other than publishing a new
one and asking players to re-subscribe.

## Wiring it to the build

```xml
<Target Name="SteamPublish">
  <Exec Command='sh .\Steam\SteamPublish.sh "$(OutputPath)"' />
</Target>
```

```bash
dotnet build MyMod.csproj -t:SteamPublish
```

`$(OutputPath)` is your mod folder, so the upload always ships exactly what the game
loads. Note the closing quote after `$(OutputPath)` — the version in the
`PlatformEfficiencyOverlay` sample is missing it.

## What gets uploaded

Everything in `contentfolder`. That should be:

```text
MyMod.dll
manifest.json
translations.json
Resources/…
```

It should **not** be game assemblies (that is what `<Private>False</Private>` prevents),
`.pdb` files unless you want them shipped, or your `Steam/` folder. Look in the output
folder before your first upload — whatever is there is what the world gets.

## Before you go public

- `manifest.json` `Version` bumped, and `GameVersionSupportRange` honest about what you
  have tested
- `Dependencies` lists ShapezShifter (`steam:3542611357`) with a version range
- `AffectsSaveGames` correct — `true` if you write save data, `false` for a read-only
  overlay
- Tested from a **subscribed copy**, not your build output. Subscribing installs to the
  Workshop content folder, which is a different path from `SPZ2_PERSISTENT\mods` and
  catches "works on my machine" path bugs

## Gotchas

- **First upload with `publishedfileid "0"`, then never again.** Leaving it at `0`
  creates a fresh item every run.
- The `cygpath`/`envsubst`/double-backslash dance in the script exists because
  `steamcmd` wants Windows paths while the script runs in bash. On Linux or macOS you
  will need to simplify those lines.
- `steamcmd` may prompt for Steam Guard on first login. Run it once interactively before
  wiring it into a build.
- Workshop descriptions take BBCode, not Markdown.
