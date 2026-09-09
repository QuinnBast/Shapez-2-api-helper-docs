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
| `mrl.reload <name>` | dispose that mod and run its rebuilt assembly |
| `mrl.run <command>` | run another command, print it, and copy its output to the clipboard |
| `mrl.copy` | put the last captured output on the clipboard again |
| `mrl.paste` | run whatever is on the clipboard as a console command |

The clipboard commands exist because the in-game console cannot be selected from, so
anything worth reading is trapped there. `mrl.run peo.types` runs that command and leaves
its output on your clipboard. Everything these commands print is mirrored into
`Player.log` as well, so output survives even if the clipboard is unavailable.

The name matches loosely against the mod's title and folder, and refuses ambiguous matches
so a typo cannot reload the wrong thing. The loop becomes:

## The locked-file problem, and the fix

The installed copy of a mod is memory-mapped the moment the game loads it, so a normal
build cannot overwrite it while the game runs — which would leave nothing new to reload.

So build with `-p:Dev=true`, which stages the output beside the mods folder instead of into
it. Mod discovery only enumerates immediate subdirectories of `mods`, so a staged build is
never loaded as a second mod, and `mrl.reload` prefers it when it is there:

```
dotnet build -p:Dev=true    # writes to <persistent>/mods-dev/<Mod>/
mrl.reload efficiency       # in game, reads the staged build
```

The reload report says which source it used and how old the staged build is, because
reloading stale bytes looks exactly like a reload that did nothing.

Without `-p:Dev=true` the build installs to `mods/` as usual, which is what you want for
the copy that loads at startup.

## Why the game cannot do this itself

`ModLoader.LoadMods()` throws on a second call — *"Trying to load mods multiple times. This
is not supported"* — and mods are loaded with `Assembly.LoadFrom`. In Unity's Mono runtime
an assembly loaded that way can never be unloaded, and calling `LoadFrom` again on the same
path returns the assembly already in memory rather than reading the file.

So this tool copies the mod's folder to a fresh path under a new name each time. The runtime
treats that as a different assembly and genuinely reads the new bytes.

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
