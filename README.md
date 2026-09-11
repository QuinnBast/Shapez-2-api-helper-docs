# shapez 2 mods

Every shapez 2 mod I maintain, plus the community modding documentation, in one place.

## What's here

| Folder | What it is |
| --- | --- |
| [Shapez2-Space-Platform-Efficiencies](Shapez2-Space-Platform-Efficiencies/) | **Platform Efficiency Viewer** — throughput and efficiency readouts for platforms |
| [Shapez2-Crossover-Platforms](Shapez2-Crossover-Platforms/) | **Crossover Platforms** — space paths that cross without mixing, placed automatically instead of lift detours |
| [Shapez2-Platform-Blackbox](Shapez2-Platform-Blackbox/) | **Platform Blackbox** — collapse a selection of platforms into one stand-in that does the same job |
| [Shapez2-Extended-Research](Shapez2-Extended-Research/) | **Extended Research** — extra research tiers and layer unlocks |
| [Shapez2-Train-Cargo-Tools](Shapez2-Train-Cargo-Tools/) | **Train Cargo Tools** — cargo belts for moving train containers between platforms |
| [Shapez2-Mod-Reloader](Shapez2-Mod-Reloader/) | **Mod Reloader** — development tool. Rebuild a mod and reload it without restarting the game |
| [Shapez2-Toolbar-Kit](Shapez2-Toolbar-Kit/) | Shared source for name-based toolbar placement, imported by the mods rather than shipped as its own assembly |
| [Shapez2-Infinite-Train-Jumps](Shapez2-Infinite-Train-Jumps/) | Design notes only, no code yet |
| [shapez2-modding-docs](shapez2-modding-docs/) | The modding documentation site, published to GitHub Pages |

## Building

Every mod targets the game's own assemblies and [Shapez Shifter](https://steamcommunity.com/sharedfiles/filedetails/?id=3542611357), so the build needs to be told where those live:

| Variable | Points at |
| --- | --- |
| `SPZ2_PATH` | `…/steamapps/common/shapez 2/shapez 2_Data/Managed` |
| `SPZ2_PERSISTENT` | `…/AppData/LocalLow/tobspr Games/shapez 2` |
| `SPZ2_SHIFTER` | the `ShapezShifter.dll` from the Shifter workshop item |

With those set, each mod builds on its own:

```sh
cd Shapez2-Crossover-Platforms
dotnet build                 # installs into <persistent>/mods/
dotnet build -p:Dev=true     # stages into <persistent>/mods-dev/ for Mod Reloader
```

The installed copy is memory-mapped while the game is running and cannot be overwritten, which
is what the staged build is for — see [Mod Reloader](Shapez2-Mod-Reloader/).

## Two things this repository deliberately does not contain

**The decompiled game assemblies.** They are tobspr's shipped code and cannot be
redistributed. They are reconstructed locally into `decompiled/`, which is gitignored; the
documentation site generates its API reference from them the same way and publishes the result
as a release asset rather than committing it.

**`shapez2-mod-samples`.** That is [tobspr's official sample repository](https://github.com/tobspr-games/shapez2-mod-samples), kept as a separate
sibling checkout so it can keep tracking upstream. Clone it beside this one if you want it.

## Documentation

The site in `shapez2-modding-docs/` is built with DocFX and deploys to GitHub Pages on any push
that touches it. Its API reference is generated from the game assemblies on a machine that has
them, uploaded as a release asset, and downloaded by CI — so nothing large or unredistributable
enters git history.

## History

This repository was assembled from several separate ones. The per-mod histories were rewritten
into their subdirectories and merged, so they are all still here — but because the merges join
unrelated histories, `git log -- <folder>` simplifies most of it away. Use `--full-history`:

```sh
git log --full-history --oneline -- Shapez2-Space-Platform-Efficiencies
git log --follow -- Shapez2-Space-Platform-Efficiencies/README.md
```

## Licence

Apache 2.0 per mod; see the `LICENSE` and `NOTICE` in each folder.
