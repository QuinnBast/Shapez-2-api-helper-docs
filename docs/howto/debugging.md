# Debug a mod

**Problem.** Your mod does nothing, and you cannot tell whether it failed to load,
failed to hook, or is running and silently wrong.

**Solution.** Work down the list below in order. Each step rules out a whole class of
failure.

## 1. Read the log

```text
%USERPROFILE%\AppData\LocalLow\tobspr Games\shapez 2\Player.log
```

`Player-prev.log` is the previous run — which is the one you want after a crash, because
the current file has already been overwritten by the restart.

Unity writes mod-load exceptions here. Tail it while the game starts:

```bash
tail -f "$SPZ2_PERSISTENT/Player.log"
```

Your own logging lands here too:

```csharp
Logger.Info?.Log("MyMod: loaded");
Logger.Warn?.Log($"MyMod: unexpected {value}");
Logger.Exception?.LogException(ex);
```

Prefix your messages with the mod name. `Player.log` is busy, and `grep MyMod` is the
difference between finding your line and scrolling.

## 2. Confirm the mod loaded at all

Log one line from the constructor. No line means the game never constructed your class:

- The DLL is not in the mod folder — check
  `%USERPROFILE%\AppData\LocalLow\tobspr Games\shapez 2\mods\<YourMod>\`
- `manifest.json` is missing, malformed, or does not list your assembly in `Assemblies`
- A dependency in `manifest.json` is not installed
- The mod is not enabled in the game's mods menu
- Your class is not `public`, or does not implement `IMod`

## 3. Use the in-game console

Press **`F1`**. Two commands earn their keep immediately:

| Command | Use |
| --- | --- |
| `debug.export-game-data` | dumps game content to JSON — definition ids, milestones, scenario data |
| `<yourmodname>.<command>` | your own commands, auto-prefixed with your assembly name |

Adding a `dump` command that prints the state your mod *believes* it has is the fastest
debug loop available — see [Add a console command](console-command.md). It beats
per-frame logging, which drowns you in `Player.log`.

## 4. Attach a debugger

Unity's mono runtime accepts a managed debugger, which gets you breakpoints and locals
instead of print statements.

- **Rider** — *Run → Attach to Unity Process*, pick the running `shapez 2`
- **Visual Studio** — install the *Visual Studio Tools for Unity* workload, then
  *Debug → Attach Unity Debugger*

Build in `Debug` and make sure the `.pdb` sits next to your `.dll` in the mod folder, or
breakpoints will not bind.

> [!NOTE]
> Attaching to a shipped Unity player is not always permitted depending on how the build
> was made. If breakpoints never bind, fall back to console commands and logging — the
> loop is slower but it always works.

## The two failures that look like nothing

These produce no exception and no log line, which is why they waste whole evenings.

### A hook that never installed

`DetourHelper` throws if it cannot resolve the target method — but only if you let it.
Wrap installation and log the outcome:

```csharp
try
{
    DrawHook = DetourHelper.CreatePostfixHook<MapDrawer, FrameDrawOptionsNoLOD>(
        (drawer, options) => drawer.Draw(options),
        (drawer, options) => Draw(options));

    Logger.Info?.Log("MyMod: draw hook installed");
}
catch (Exception ex)
{
    Logger.Exception?.LogException(ex);   // the method moved or was renamed
}
```

If the hook installed but your code never runs, the method is not being called — you
targeted an overload or a code path the game does not take.

### A publicizer that did not run

Symptom: it compiles, then throws `FieldAccessException` or `MethodAccessException` at
runtime when touching a `private` member.

Check for this at build time:

```text
warning : Assembly is marked for publicization, but no members were publicized
```

That means references are not resolving — usually `SPZ2_PATH` is unset or wrong. See
[The Publicizer](../publicizer.md).

## Common symptoms

| Symptom | Likely cause |
| --- | --- |
| `NullReferenceException` in the constructor | you touched the map at mod-load time — [run code when a game loads](run-code-when-game-loads.md) |
| New building missing from the toolbar | no research unlock, or wrong toolbar index — [unlock](add-research-unlock.md), [toolbar](add-to-toolbar.md) |
| Name shows as `my-mod.thing.title` | `translations.json` did not copy, or key is misspelled — [translations](add-translations.md) |
| Works in one scenario, not another | hard-coded milestone id — [per-scenario selection](add-research-unlock.md#unlock-at-a-milestone) |
| Machines stop working near your code | you replaced a lane hook instead of chaining it — [read machine state](read-machine-state.md#gotchas) |
| Framerate collapses | per-frame work over every building — [pacing](run-code-when-game-loads.md#do-not-do-heavy-work-every-tick) |
| Worked before a game update | your detour target changed — [staying compatible](../hooking.md#staying-compatible) |

## Iterating faster

- **Rebuild installs.** `OutputPath` points at the mod folder, so a build *is* an
  install. There is no hot reload — restart the game.
- **Keep a small test save.** Loading a 10,000-building megabase to test a two-line
  change costs more than the change.
- **Log state transitions, not frames.** "overlay enabled", "map loaded, 42 islands" —
  one line per event, not per tick.
