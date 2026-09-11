# shapez 2 modding workspace

One folder per repo. Work happens in the `Shapez2-*` mod repos; the other three are
references.

| Folder | What it is |
| --- | --- |
| `shapez2-modding-docs/` | **Community modding docs. Read these first, and keep them updated.** |
| `decompiled/` | Decompiled game + ShapezShifter assemblies. The source of truth for behaviour. |
| `shapez2-mod-samples/` | Fork of the official samples. Reference only — don't add files here. |
| `Shapez2-Toolbar-Kit/` | Shared *source* (not an assembly) for name-based toolbar placement. |
| `Shapez2-Train-Cargo-Tools/` | Cargo packagers, belts, stores; detours so stations eat packages. |
| `Shapez2-Crossover-Platforms/` | Belt/pipe crossings. |
| `Shapez2-Platform-Blackbox/` | Collapsing platform selections into blueprints. |
| `Shapez2-Space-Platform-Efficiencies/` | Throughput HUD overlay (shipped). |
| `Shapez2-Extended-Research/` | Extra research tiers. |
| `Shapez2-Mod-Reloader/` | Hot reload during development. |
| `Shapez2-Infinite-Train-Jumps/` | DESIGN.md only, no code. |

## Use the docs, and add to them

`shapez2-modding-docs/` is the accumulated answer to "how does this actually work".
Before investigating a modding question from scratch, **search there first** — several
things that look like fresh discoveries are already written up, sometimes more thoroughly.

```
grep -rn "<thing>" shapez2-modding-docs/docs/
```

Layout: `docs/*.md` are concept pages (architecture, hooking, coordinates, map model);
`docs/howto/*.md` are task pages; `docs/toc.yml` is the nav. `api/` is DocFX-generated —
never hand-edit it.

**When you learn something non-obvious about the game or ShapezShifter, write it down
there.** That is the point of the repo. Prefer extending an existing page over adding
one; add a `toc.yml` entry only if you create a page. Match the house voice: state the
problem, then the mechanism, then the consequence. Say *why*, and name the concrete
class or method that proves it.

If a page's code sample contradicts a warning further down, fix the sample — people copy
samples.

## Finding out how the game works

`decompiled/` holds the decompiled assemblies. `grep -rn` there answers most questions
faster than guessing.

**What is not in there:** authored ScriptableObject data. Island definitions for train
stations, toolbar structure, base processing durations, scenario JSON — all authored, so
you cannot read their values statically. Don't guess a constant and ship it; either
source it at runtime from `GameMode`/config, or say you couldn't and leave it out. A
confidently wrong number in the UI is worse than no number.

## Build and install

Three env vars drive the projects: `SPZ2_PATH` (game install), `SPZ2_PERSISTENT`
(`~/AppData/LocalLow/tobspr Games/shapez 2`), `SPZ2_SHIFTER` (ShapezShifter.dll).

```bash
dotnet build                 # installs into <persistent>/mods/<Mod>      (game must be CLOSED)
dotnet build -p:Dev=true     # stages into <persistent>/mods-dev/<Mod>    (safe while running)
```

**Always check whether the game is running before building**, because the failure mode is
confusing:

```powershell
Get-Process | Where-Object { $_.ProcessName -match 'shapez' }
```

The installed dll is memory-mapped while the game runs, so a plain build fails with
`MSB3027 / user-mapped section open` — note that this is the *copy* failing, not a
compile error.

The game loads from `mods/`. `mods-dev/` only exists for the reloader. It is easy to
"fix" something, stage it, and have the user still running the old build — **verify what
is actually installed**:

```bash
grep -o '"Version": "[^"]*"' "$SPZ2_PERSISTENT/mods/<Mod>/manifest.json"
```

### Hot reload

`mrl.reload <modname>` in-game picks up `mods-dev/`. It reloads **code only**:

- Logic changes, detours, HUD providers → reload works.
- New islands, new toolbar entries, collider or definition changes → **restart required.**
  Definitions and the toolbar are built per session, and `AtomicIslands…Build()` returns
  no handle so a mod cannot unregister them in `Dispose`. Reloading then re-entering a
  session double-registers and can crash on a duplicate key.

Log: `$SPZ2_PERSISTENT/Player.log`. Read it before theorising about a crash.

## Conventions in these repos

- **XML doc comments explain *why*, not what.** Match the surrounding density. Comments
  that record a constraint you discovered ("this is the only option because X throws")
  are the valuable ones.
- Each mod has a `DESIGN.md` recording verified facts with the class/method that proves
  them. Keep it current when behaviour changes.
- `Directory.Build.props` adds the `-p:Dev=true` staging mode; Apache 2.0 `LICENSE` +
  `NOTICE`; `PublicizeAll` with a `DoNotPublicize` list; `NoWarn=CS0436`.
- Krafs.Publicizer **works** despite the build warning "Assembly is marked for
  publicization, but no members were publicized". Private game members are callable.
  Verify with a throwaway probe file rather than trusting the warning.
- Shared code is distributed as **source, not assemblies** — see
  `Shapez2-Toolbar-Kit/ToolbarKit.props`. Two copies of one dll in the same AppDomain, or
  a mod that hard-depends on another mod, are both problems worth avoiding for ~100 lines.

## Other agents edit these repos concurrently

More than one session works here at once. Files change under you.

- Before editing a file you last read a while ago, re-read it.
- If a build fails in a file you never touched, check `ls -la --time-style=+%H:%M` before
  assuming you broke it — and don't "fix" someone's in-flight edit.
- Several fixes have already been applied by another session (the `ModResources` hot-reload
  fix, the per-chunk collider, the docs' collider section). **Check before doing work
  twice.**

## Traps that have already cost a session

Each is written up properly in the docs; this is the index.

| Trap | Page |
| --- | --- |
| `WithBoundingCollider()` makes a **zero-sized** box — island cannot be clicked. Always `WithPerChunkColliders()`. | `howto/add-an-island.md` |
| MonoMod cannot hook a method on a **generic type**, struct instantiation included. Compiles, then kills startup. Relocate to a non-generic choke point. | `hooking.md` |
| Toolbar ids end in `.title`, casing is inconsistent, and separators are skipped resolving the parent but counted for the leaf index. | `howto/add-to-toolbar.md` |
| `Bind("x", n.ToString().T())` renders `?1` — `.T()` means *translation id*. Use `RawText`. A `?` prefix anywhere means a failed lookup. | `howto/add-translations.md` |
| No stock side-panel module renders an arbitrary **live number**; text is set once. `HUDSidePanelModuleRocketProgress` is the generic current/max bar despite the name. | `howto/island-side-panel.md` |
| Pipette map is a `Dictionary.Add`; a second placer over the same definitions crashes startup. | `howto/add-an-island.md` |
| `ModDirectoryLocator` throws on hot reload — a byte-loaded assembly has an empty `Location`. | `howto/load-models-and-icons.md` |
| `SyncableIdentifier` is read with `inherit: false`, so every saved state needs its own concrete type and id. | — |
| `translations.json` is keyed by **language code first**; placeholders are self-closing `<name/>`. Either mistake aborts mod load. | `howto/add-translations.md` |
| `AffectsSaveGames: true` means the mod cannot be added to or removed from an existing save. | — |

## Working style the user expects

- Say what is verified versus what is untested. Nothing here can be runtime-tested by the
  agent — the user tests. Be explicit about which parts have only been compiled.
- When a fix is guessed, say so and name the discriminating observation that would confirm it.
- Correct your own earlier wrong claims plainly when evidence contradicts them.
- Prefer finishing one thing properly over starting five.
