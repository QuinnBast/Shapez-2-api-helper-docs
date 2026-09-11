# Mod Reloader

A development tool. Rebuild a mod, run one console command, and its new code runs — no game
restart.

On a large save that is the difference between a two-minute wait and a second, which is the
whole point: the game itself has no reload, and its loader actively refuses a second load.

> **Not for shipping.** Every reload leaks an assembly, and reloading only works cleanly if
> the mod being reloaded undoes everything in `Dispose`. Keep this in your dev mods folder.

## Using it

In the debug console (**F1**):

| | |
|---|---|
| `mrl.list` | every loaded mod, with every name `mrl.reload` will accept |
| `mrl.reload <name>` | save, run that mod's rebuilt assembly, and rebuild the session |
| `mrl.watch` | reload by itself whenever a staged build changes (toggle) |
| `mrl.run <command>` | run another command, print it, and copy its output to the clipboard |
| `mrl.copy` | copy the last command's output, whatever command it was |
| `mrl.paste` | run whatever is on the clipboard as a console command |
| `mrl.seed` | install any staged build that has no installed copy yet |

`mrl.reload` saves the game, swaps the code, and then rebuilds the game session by
re-entering that save. The rebuild is what makes new *content* appear: buildings and
islands are baked in `GameMode.From`, meshes and animations into the session's own
`MeshCache`, the toolbar in `ToolbarBuilder.BuildToolbar`, panels and commands in the
orchestrator's `Init_` steps - all per session, from the vanilla baseline, with Shifter's
interceptors consulting your mod on the way past. Swapping the assembly alone cannot touch
any of it, because it was already built by the old code.

The save has to come first: the old instance is the only code that can still write its own
save data, and without it everything since the last save would be rolled back by the reload
that follows. If there is no session to rebuild - the main menu's background map, or a save
that would not write - the reload stays a code-only one and says so, and then puts the new
code back in front of the session callbacks that only fire once at init, which no mod can
do for itself. Without that a reloaded mod's commands keep answering from the disposed
instance.

`mrl.watch` runs that loop for you: it watches the staged build folder and reloads whatever
was rebuilt, so a `dotnet build -p:Dev=true` in another window is the whole gesture. A build
is several writes, so a change only counts once the folder has been quiet for a moment; a
solution-wide build that stages every mod is one save and one session rebuild, not one per
mod. The report goes to `Player.log`, and stays on `mrl.copy` - the rebuilt session takes
the console's history with it.

The clipboard commands exist because the in-game console cannot be selected from, so
anything worth reading is trapped there. `mrl.run peo.types` runs that command and leaves
its output on your clipboard. `mrl.copy` does the same afterwards for whatever you ran
last - including the game's own commands and other mods' - so output only worth keeping
once you have seen it does not have to be produced twice. Everything these commands print
is mirrored into `Player.log` as well, so output survives even if the clipboard is
unavailable.

The name matches loosely against the mod's title and folder, and refuses ambiguous matches
so a typo cannot reload the wrong thing. The loop becomes:

## Setting up a mod for reloading

The installed copy of a mod is memory-mapped the moment the game loads it, so a normal
build cannot overwrite it while the game runs - there would be nothing new to reload. So a
mod being developed needs to build somewhere else: **`<persistent>/mods-dev/<ModName>/`**.

Mod discovery only enumerates immediate subdirectories of `mods`, so a staged build there
is never loaded as a second mod, and `mrl.reload` prefers it when it finds one.

The one-time setup, for any mod:

```xml
<PropertyGroup Condition="'$(Dev)' == 'true'">
    <OutputPath>$(SPZ2_PERSISTENT)\mods-dev\$(MSBuildProjectName)\</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
</PropertyGroup>
```

Then the loop is:

```
dotnet build -p:Dev=true    # the game can stay open; nothing locked is touched
mrl.reload mymod            # in game
```

`Directory.Build.props.template` in this repo is the same thing as a drop-in file: rename it
to `Directory.Build.props` beside your solution and MSBuild imports it automatically, with
no csproj edit at all.

## The first build of a new mod

A staged build with no installed copy beside it is invisible: the game never discovers it,
so there is nothing for `mrl.reload` to replace. Mod Reloader copies such a build into the
mods folder at startup, and `mrl.seed` does the same on demand.

A mod already running from somewhere else - a workshop subscription, most likely - is left
alone: a second copy would give the loader two mods with the same id, and someone who
deleted their local copy to test the published one should not find it put back.

The copy cannot take effect in the session that makes it - discovery runs before any mod's
code, this one included - so it loads on the next launch. That is the point of copying
rather than loading the assembly directly: the game then treats it as an ordinary installed
mod, applies the usual manifest, game-version and dependency checks, and reports a failure
in the mod list exactly as it would for anything else. From then on it reloads normally.

The reload report names the source it used and how old the staged build is, because
reloading stale bytes looks exactly like a reload that did nothing. Reloading straight from
the installed folder says so outright rather than appearing to work.

## Why the game cannot do this itself

`ModLoader.LoadMods()` throws on a second call — *"Trying to load mods multiple times. This
is not supported"* — and mods are loaded with `Assembly.LoadFrom`. In Unity's Mono runtime
an assembly loaded that way can never be unloaded, and `LoadFrom` binds by assembly
*identity* — name, version, culture, public key — not by path. A rebuilt mod keeps the same
identity, so `LoadFrom` returns the copy already in memory and never reads the new file, no
matter what path it is given.

So this tool reads the rebuilt DLL into a byte array and loads it with `Assembly.Load(byte[])`.
That overload has no identity/path context: it takes the raw image and genuinely reads the
new bytes as a distinct assembly every time, even when the identity is unchanged.

## What that costs

These are consequences of the approach, not bugs to be fixed:

- **Each reload leaks an assembly.** A few MB across a session's worth of reloads. Fine to
  develop against, never to ship.
- **Old and new types are different types.** Anything the game still holds from the previous
  assembly keeps the old code alive.
- **A mod is only as reloadable as its `Dispose`.** Whatever it registered and did not undo
  — a HUD button, an event subscription, a lane hook — survives and then exists twice. This
  is the usual cause of a reload that "half worked".

That last point is the useful side effect: it makes incomplete teardown visible immediately,
and a mod that cannot clean up after itself also cannot be disabled mid-session.

## State

Early. `mrl.list` reads the loader's own lists; `mrl.reload` performs the dispose,
shadow-copy, load and construct sequence, mirroring what `ModLoader` does
(`DependencyContainer` with `ILogger` bound, then `Create` on the single `IMod`). Reporting
is deliberately verbose so a failure says which step failed and whether the old instance is
still running.

Untested against a real reload so far.

## Building

Set `SPZ2_PATH`, `SPZ2_PERSISTENT` and `SPZ2_SHIFTER`, then `dotnet build`. Output goes to
the mods folder.

Licensed under [Apache 2.0](LICENSE).
