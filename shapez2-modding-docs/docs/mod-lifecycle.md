# Mod Lifecycle

> **You need this when** something is unexpectedly null, or you want your code to run
> at the right moment. For the copy-pasteable version, see
> [Run code when a game loads](howto/run-code-when-game-loads.md).


## `IMod`

A mod is a public class implementing `IMod`. It is discovered by reflection, so nothing
references it directly — mark it `[UsedImplicitly]` to stop your IDE calling it dead
code.

```csharp
[UsedImplicitly]
public class MyMod : IMod
{
    public MyMod(ILogger logger) { /* entry point */ }
    public void Dispose()        { /* exit point  */ }
}
```

The **constructor is the entry point**. There is no `Init()` — whatever you want to
happen, happens there or is registered there.

## Constructor injection

Constructor parameters are resolved by the mod loader. `Core.Logging.ILogger` is the
one every mod uses:

```csharp
using ILogger = Core.Logging.ILogger;
```

That `using` alias is not optional in practice — `ILogger` is ambiguous with Unity's
and Microsoft's, and every sample mod aliases it the same way.

Logging is null-conditional by design, so a disabled level costs nothing:

```csharp
Logger.Info?.Log("loaded");
Logger.Warn?.Log($"unexpected: {value}");
Logger.Exception?.LogException(ex);
```

## What exists at construction time

This is the single biggest source of "why is this null" in new mods.

Your constructor runs at **mod load**, which is long before a save is opened. At that
moment there is no map, no islands, no simulator, and no player. What you *can* do is:

- register content with [Flow](architecture.md#flow--shapezshifterflow) builders
- register rewirers / interceptors
- install detour hooks
- read your own files from disk

What you cannot do is touch a game session. Anything session-shaped has to wait for a
session to exist.

## Reaching the running session

Once a game is running, the convenient accessor is:

```csharp
using ShapezShifter.Kit;

IGameSessionManagers core = GameHelper.Core;
IMapModel map = core.LocalPlayer.CurrentMap;
```

`IGameSessionManagers` exposes `LocalPlayer`, `Savegame`, `Mode`, `Viewport`,
`ShapeRegistry`, `FluidRegistry`, `SimulationSpeed`, `Research`, `ExpiringResources`,
`AudioManager`, `InteractionMode`, `HubObserver`, `EntityPlacementRunner`, and
`DataSerializers`.

> [!WARNING]
> `IGameSessionManagers` is marked `[Obsolete("Inject only what you need")]`. It still
> works and it is by far the easiest way in, but the game's own code is moving to
> constructor injection. Expect it to be the first thing that breaks in a future
> version, and keep your use of it in one place so there is one thing to fix.

Guard every access — outside a session `LocalPlayer` or `CurrentMap` can be `null`:

```csharp
IMapModel map = GameHelper.Core?.LocalPlayer?.CurrentMap;
if (map == null) return;
```

`Player.OnMapChanged` is an event you can register on if you need to react to a map
being loaded or swapped.

Note that some hooks hand you the session objects directly, which is cleaner than the
static accessor when available — a draw hook, for instance, receives
`FrameDrawOptionsNoLOD.Player`, and so already has `Player.CurrentMap` in hand without
any global lookup.

## Per-frame work

The one-liner, from `ShapezShifter.Flow.GameFlowExtensions`:

```csharp
using ShapezShifter.Flow;

public MyMod(ILogger logger)
{
    RewirerHandle handle = this.OnTick(deltaTime => Update(deltaTime));
}
```

Under the hood this registers an `ActionTickRewirer`, driven by `TickInterceptor`,
which is a postfix hook on `GameSessionOrchestrator.Tick(float)`. Two consequences
worth knowing:

- It only fires **while a session is running** — no session, no ticks. That makes it a
  safe place to do session work, but still null-check the map.
- `deltaTime` is real frame time, not simulation time. It does not account for the
  game's simulation speed multiplier. If you need simulation time, read it from the
  simulator (`ISimulator.GetSimulationTimeFor`) or from
  `FrameDrawOptionsNoLOD.SimulationTime_G` in a draw hook.

Do not do heavy work every tick. A sweep over every building on a large map is far too
expensive at 60 Hz; accumulate `deltaTime` and run every few hundred milliseconds, or
process a slice of the work per tick.

```csharp
private float Accumulator;

private void Update(float deltaTime)
{
    Accumulator += deltaTime;
    if (Accumulator < 0.25f) return;
    Accumulator = 0.0f;
    // …the expensive pass, four times a second
}
```

## Disposal

`Dispose()` runs when the mod is unloaded. Undo anything global you installed:

```csharp
public void Dispose()
{
    DrawHook?.Dispose();      // MonoMod Hook
    TickHandle?.Dispose();    // RewirerHandle
    CachedMeshes?.Dispose();  // TemporaryMeshReference and friends
}
```

Leaking a hook or a mesh will not always be obvious in a single session, but it shows
up as duplicated behaviour after a mod reload, or as Unity warnings about undisposed
meshes.
