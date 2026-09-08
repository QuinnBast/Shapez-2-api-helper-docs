# Add a simulation system

**Problem.** You want behaviour that is not tied to one building instance — something
that runs every tick across many entities, or maintains its own graph.

**Solution.** A simulation *system*. Buildings have simulations; systems own and update
them.

## The distinction

| | Simulation | System |
| --- | --- | --- |
| Scope | one placed entity | all entities of a kind |
| Created by | a system, on placement | the session, once |
| Example | `HalfCutterSimulation` | `FluidNetworkSimulationSystem`, `TrainSystem` |

If you are adding a machine, you do **not** need to write a system by hand — Flow's
`.WithSimulation(factoryBuilder, logger)` registers one for you
([add a building](add-a-building.md)). Write a system directly when your behaviour spans
entities: a network, a global tracker, a cross-platform mechanic.

## The contract

```csharp
public interface ISimulationSystem
{
    IEvent<IConnectableSimulation> OnSimulationCreated { get; }
    IEvent<IConnectableSimulation> OnBeforeSimulationDestroyed { get; }
    IEnumerable<IConnectableSimulation> ConnectableSimulations { get; }
}
```

That is the minimum — enumerate your simulations and announce lifecycle. Systems that
tick also implement `IUpdateableSimulationSystem` (`Update(Ticks startTicks, Ticks
deltaTicks)`); ones keyed by chunk implement `IChunkSimulationSystem`
(`TryGetChunkSimulation`); ones that attach to islands implement
`IIslandTenantSimulationSystem` (`IslandIsOffered` / `IslandOfferIsTerminated`).

`TrainSystem` implements all four and is the best worked example in the game of a
non-trivial system.

## Registering one

```csharp
using ShapezShifter.Hijack;

public class MySystemsRewirer : ISimulationSystemsRewirer
{
    public void ModifySimulationSystems(
        ICollection<ISimulationSystem> simulationSystems,
        SimulationSystemsDependencies dependencies)
    {
        simulationSystems.Add(new MySystem(dependencies.ShapeRegistry, dependencies.Logger));
    }
}

// in your IMod constructor:
RewirerHandle handle = GameRewirers.AddRewirer(new MySystemsRewirer());
```

You receive the live collection, so you can add, and in principle remove or replace, an
existing system. Removing a vanilla system is a blunt instrument — it will break every
building that depends on it — but it is the mechanism if you genuinely need to replace
one wholesale.

## What you get handed

`SimulationSystemsDependencies` is everything a system might need, which doubles as a
map of what the simulation layer is built from:

```csharp
public readonly Ticks InitialSimulationTime;
public readonly GameMode Mode;
public readonly IShapeRegistry ShapeRegistry;
public readonly IShapeIdManager ShapeIdManager;
public readonly IGameResourcesMap ResourcesMap;
public readonly IFluidRegistry FluidRegistry;
public readonly FluidPackageItemSolver FluidPackagesItem;
public readonly ISignalChannelRegistry SignalChannelRegistry;
public readonly IResearchUnlockManager ResearchUnlockManager;
public readonly ILogger Logger;
```

`InitialSimulationTime` matters for anything time-based: a system created when loading a
save must not assume time starts at zero.

## Finding systems at runtime

```csharp
ISimulator simulator = map.Simulator;

MySystem system = simulator.GetSystem<MySystem>();
if (simulator.TryGetSystem<MySystem>(out MySystem s)) { … }

foreach (IUpdateableSimulationSystem u in simulator.GetSystems<IUpdateableSimulationSystem>()) { … }
```

This also works for **vanilla** systems, which is often all you need: reading
`FluidNetworkSimulationSystem` or `TrainSystem` requires no system of your own.

## Determinism

Simulation systems run inside the game's deterministic tick. Two rules:

- **No `Random` without a seeded, serialised generator**, and no wall-clock time.
  `Update` gives you `Ticks`; use them.
- **No frame-rate dependence.** `deltaTicks` is simulation time, not frame time.

Breaking either produces the worst class of bug in this game: a save that replays
differently than it ran.

## Gotchas

- Ticking over every entity every tick is the most expensive thing a mod can do. Keep
  per-tick work proportional to what actually changed, and lean on
  `OnSimulationCreated` / `OnBeforeSimulationDestroyed` to maintain incremental state.
- Anything your system holds that must survive a reload belongs in a serialised
  `ISimulationState` on an entity, or in
  [mod save data](save-data.md) — not in system fields.
- Dispose the `RewirerHandle` in `IMod.Dispose()`, and make your system `IDisposable`
  if it holds anything.

> [!NOTE]
> No sample mod adds a bare simulation system — the samples all go through Flow. This
> page is derived from the interfaces and from `TrainSystem`; verify the lifecycle
> callbacks you rely on against your game version.
