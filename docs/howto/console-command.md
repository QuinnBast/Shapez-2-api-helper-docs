# Add a console command

**Problem.** You want to toggle, dump, or inspect something without rebuilding — the
fastest debugging loop available to a mod.

**Solution.** ShapezShifter has a one-call helper.

```csharp
using ShapezShifter.Flow;

public MyMod(ILogger logger)
{
    ModConsoleCommandsCreator.ModConsoleRewirer commands = this.AddModCommands();

    commands.AddCommand(console =>
        console.Register("toggle", context =>
        {
            Enabled = !Enabled;
        }));

    commands.AddCommand(console =>
        console.Register("dump", context =>
        {
            IMapModel map = GameHelper.Core?.LocalPlayer?.CurrentMap;
            Logger.Info?.Log($"islands: {map?.IslandCount ?? 0}");
        }));
}
```

`AddModCommands()` returns a rewirer that is registered for you and is `IDisposable` —
dispose it in `IMod.Dispose()`.

Your commands are **automatically prefixed with your assembly name**, lower-cased. A
mod called `PlatformEfficiencyOverlay.dll` registering `toggle` gives the player
`platformefficiencyoverlay.toggle`. That prevents collisions between mods, and it means
short command names are safe.

## Registering without the prefix

The prefix is good hygiene but long: `platformefficiencyoverlay.toggle` is a lot to type
while iterating. Implementing `IConsoleRewirer` yourself skips the wrapper that adds it, so
you can choose a short prefix of your own:

```csharp
public class MyCommands : IConsoleRewirer
{
    public void RegisterCommands(IDebugConsole console)
    {
        console.Register("peo.report", context => context.Output?.Invoke(Describe()));

        console.Register("peo.alpha", new DebugConsole.FloatOption("alpha", 0f, 1f),
            context => Tuning.Alpha = context.GetFloat(0));
    }
}

GameRewirers.AddRewirer(new MyCommands(logger));
```

`context.Output` is an `Action<string>` field and can be null, so invoke it conditionally.
Wrap each `Register` in its own `try`/`catch` if you register several — the Shifter catches
per rewirer, so one failure otherwise takes the rest of your commands with it.

## Why this matters more than it looks

A mod DLL is memory-mapped by Mono once loaded, so **new code needs a game restart** —
there is no hot reload in the Shifter or the game. Anything you would otherwise tune by
rebuilding is worth exposing as a command instead: colours, thresholds, sizes, heights,
and any "is this measurement right?" dump.

That turns a five-minute rebuild-and-reload cycle into typing a number, which is the
difference between tuning something properly and settling for the first value that looked
acceptable. A `report` command that prints all current values pays for itself immediately.

## Command signatures

```csharp
void Register(string id, Action<DebugConsole.CommandContext> handler, bool isCheat = false);
void Register(string id, DebugConsole.ConsoleOption option0, Action<…> handler, bool isCheat = false);
void Register(string id, DebugConsole.ConsoleOption option0, DebugConsole.ConsoleOption option1, Action<…> handler, bool isCheat = false);
```

Up to two declared options; read their values from the `CommandContext` in the handler.
`isCheat: true` marks the command as a cheat, with whatever consequences the game
attaches to that — leave it `false` for inspection commands.

## Why this beats logging

## `debug.export-game-data`

Before writing a command of your own, know that the game ships one that answers most
"what is this thing called?" questions:

```text
F1
debug.export-game-data
```

It dumps the game's content as JSON — building and island definition ids, research
milestones, scenario data. That is where you get the exact strings the content builders
want, instead of guessing at `BuildingDefinitionId` names. See
[Find the ids you need](find-buildings.md#finding-definition-ids).


The command handler runs inside a live session with everything reachable, so it is a
REPL you can point at your own code. A `dump` command that prints the state your mod
believes it has is usually the fastest way to find the bug — much faster than
`Logger.Info` in a per-frame path, which drowns you.

## Gotchas

- The console needs to be open for commands to be reachable; it is a developer feature
  and may need enabling depending on the build.
- **`F1` opens the console.** Vanilla debug commands live under `debug.…`, and
  `debug.export-game-data` is the one to remember — see below.
- Registration happens through a rewirer, so commands appear when a session initialises
  the console — not at mod-load time.
- Keep command handlers defensive. `CurrentMap` can be `null` and a throwing handler is
  a poor debugging experience.
