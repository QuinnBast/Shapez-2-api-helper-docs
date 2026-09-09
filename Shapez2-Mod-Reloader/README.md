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
| `mrl.list` | every loaded mod, its version, entry point and assembly file |
| `mrl.reload <name>` | dispose that mod and run its rebuilt assembly |

The name matches loosely against the mod's title and folder, and refuses ambiguous matches
so a typo cannot reload the wrong thing. The loop becomes:

```
dotnet build          # in the mod you are working on
mrl.reload efficiency # in game
```

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
