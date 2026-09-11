# Work with trains

**Problem.** You want to read where trains are, what they are carrying, or draw
something on them.

Trains are the most complex subsystem in the game — roughly 25 dedicated types before
you count predictions. The good news is that reading them is easy; extending them is
not.

## The system

```csharp
TrainSystem trainSystem = map.Simulator.GetSystem<TrainSystem>();
```

`TrainSystem` implements four interfaces at once — `IChunkSimulationSystem`,
`IUpdateableSimulationSystem`, `IIslandTenantSimulationSystem`, `ISimulationSystem` —
which makes it the best worked example in the game of a non-trivial simulation system
([add a simulation system](add-simulation-system.md)).

What it exposes:

```csharp
public readonly TrainsWagonCargo CargoSimulator;
public readonly TrainWagonCargoTypeId FluidWagonType;
public readonly TrainWagonCargoTypeId ShapeWagonType;

public CargoExchangingOrchestrator CargoExchanger { get; }
public TrainsSimulation TrainsSimulation { get; }

public bool TryGetChunkSimulation(in GlobalChunkCoordinate position, out ILocalizedChunkSimulation chunkSimulation);
```

Two cargo types, `ShapeWagonType` and `FluidWagonType` — trains carry shapes or fluids,
nothing else.

## Train data

```csharp
public readonly struct TrainData
{
    public readonly RailColor Color;
    public readonly NativeArray<WagonNavigationData>.ReadOnly Wagons;
    public readonly float Progress;

    public WagonNavigationData Head { get; }
    public WagonNavigationData Tail { get; }
}
```

- **`Wagons`** is a `NativeArray<…>.ReadOnly` — Unity Collections, not a managed array.
  Do not copy it per frame, and do not hold it beyond the callback that gave it to you.
- **`Color`** is the rail colour, i.e. which network the train belongs to.
- **`Progress`** is position along its route; `Head` and `Tail` are the ends.

`TrainId` is the identity — a `readonly struct` with an `Invalid` sentinel, in the same
family as `BuildingId` and `IslandId`.

## Drawing on trains

There is a purpose-built draw hook:

```csharp
public delegate void DrawTrainDelegate(
    FrameDrawOptionsNoLOD options,
    TrainId trainId,
    TrainData trainData,
    IDictionary<int, Matrix4x4> wagonsMatricesMap);
```

```csharp
DrawHooks hooks = drawManager.Hooks;
hooks.OnDrawTrain = (DrawHooks.DrawTrainDelegate)Delegate.Combine(
    hooks.OnDrawTrain, new DrawHooks.DrawTrainDelegate(DrawTrain));
```

`wagonsMatricesMap` hands you a ready-made transform per wagon index, so drawing a
marker above a wagon requires no coordinate maths — take the matrix, offset it, submit
your mesh ([draw in the world](draw-in-world.md)).

The whole `DrawHooks` class is `[Obsolete]` in favour of `IMapSubDrawer`, and this
particular member is marked "use the TrainDrawer instead" — but it remains the shortest
route, and `Delegate.Combine` (never assignment) keeps you from stomping other
listeners.

## Cargo

The loading/unloading chain, in the order cargo moves:

| Type | Role |
| --- | --- |
| `TrainCargoLoaderSimulation` | belt → wagon |
| `TrainCargoUnloaderSimulation` | wagon → belt |
| `TrainCargoTransferrerSimulation` | wagon → wagon |
| `TrainBeltToCargoFillingContainer` | the filling buffer, inbound |
| `TrainCargoToBeltFillingContainer` | the filling buffer, outbound |
| `TrainCargoExchangerState`, `TrainCargoTransferState` | serialised state |
| `CargoExchangingOrchestrator` | coordinates exchanges |

Every one has a prediction counterpart (`TrainCargoLoaderPredictionSubSimulationSystem`
and friends) — a reminder that a train station's placement preview is its own simulation
([placement predictions](placement-predictions.md)).

## Speed

Trains scale with a builtin research speed id:

```csharp
BuiltinResearchSpeed.TrainSpeed
```

See [speed upgrades and buffs](speed-upgrades-and-buffs.md).

## Adding train content

Rail-based islands have their own placement rewirer interface,
`ITrainIslandPlacementRewirers`, alongside the platform and converter ones — so a
train-related island goes through the island path
([add an island](add-an-island.md)) with that rewirer family.

Beyond placing an island, adding genuinely new train behaviour means participating in
the cargo exchange and prediction chains above. There is no sample and no fluent API for
it.

## Gotchas

- **`NativeArray` discipline.** `Wagons` is unmanaged memory owned by the simulation.
  Read it inside the callback; never cache it.
- Rail colour is the network identity. Iterating "all trains" without filtering by
  colour mixes unrelated lines.
- Trains are chunk simulations, not tile simulations — use
  `TryFindChunkSimulation` / `TryGetChunkSimulation`, not the tile lookup
  ([read machine state](read-machine-state.md)).
- Train state is deep and heavily predicted. Modifying it is the most likely place in
  this API to desynchronise a save.

> [!NOTE]
> The types and signatures here are verified, but no sample mod touches trains. Reading
> position and cargo is safe; writing to the train simulation is unexplored territory.
