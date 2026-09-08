# Run code when a game loads

**Problem.** Your `IMod` constructor runs at *mod load*, which is at the main menu.
There is no map, no islands, no simulator, and no player yet. Anything you write there
that touches the world gets a `NullReferenceException`.

**Solution.** Register a tick callback and wait for a map to appear.

## The pattern

```csharp
using ShapezShifter.Flow;
using ShapezShifter.Kit;
using ILogger = Core.Logging.ILogger;

[UsedImplicitly]
public class MyMod : IMod
{
    private readonly ILogger Logger;
    private readonly RewirerHandle TickHandle;
    private IMapModel ActiveMap;

    public MyMod(ILogger logger)
    {
        Logger = logger;
        TickHandle = this.OnTick(Tick);   // fires only while a session is running
    }

    private void Tick(float deltaTime)
    {
        IMapModel map = GameHelper.Core?.LocalPlayer?.CurrentMap;

        if (map == ActiveMap) return;     // nothing changed

        if (map == null)
        {
            OnGameClosed();
            ActiveMap = null;
            return;
        }

        ActiveMap = map;
        OnGameLoaded(map);
    }

    private void OnGameLoaded(IMapModel map)
    {
        Logger.Info?.Log($"Game loaded: {map.IslandCount} islands");
        // safe from here: islands, buildings, simulator, events
    }

    private void OnGameClosed()
    {
        // drop caches keyed by map contents
    }

    public void Dispose() => TickHandle?.Dispose();
}
```

Comparing against the last map you saw handles all three transitions — load, quit to
menu, and load a *different* save — with one branch. Do not use a `bool initialized`
flag; it silently keeps stale state when the player loads a second save.

## Why not just use an event?

`Player.OnMapChanged` exists and does fire on map changes:

```csharp
GameHelper.Core.LocalPlayer.OnMapChanged.Register(OnMapChanged);
```

The catch is that you need a `Player` to register on, and at mod-load time there isn't
one. So you would poll for the player in order to register for the map — at which point
the poll above is simpler. Use the event only once you already hold a live `Player`.

## Do not do heavy work every tick

`OnTick` fires every frame. A sweep over every building on a large save is far too
expensive at 60 Hz. Accumulate and run on an interval:

```csharp
private float Accumulator;

private void Tick(float deltaTime)
{
    // …map-change check from above…

    Accumulator += deltaTime;
    if (Accumulator < 0.25f) return;
    Accumulator = 0.0f;

    ExpensivePass(ActiveMap);   // four times a second
}
```

For anything per-frame *and* expensive, process a slice per tick instead — a few
hundred buildings each frame, round-robin, so every building is covered every couple of
seconds.

## Gotchas

- `deltaTime` is **real** frame time. It ignores the game's simulation speed setting. If
  you need simulation time, use `ISimulator.GetSimulationTimeFor(...)` or
  `FrameDrawOptionsNoLOD.SimulationTime_G`.
- `GameHelper.Core` is `[Obsolete("Inject only what you need")]`. It works and it is the
  easiest way in, but keep it to one place so a future break is one fix.
- Hooks you install *per session* (lane hooks, event registrations) must be removed in
  `OnGameClosed`, or they will leak into the next save — and lane hooks will hold a
  reference to a map that no longer exists.
