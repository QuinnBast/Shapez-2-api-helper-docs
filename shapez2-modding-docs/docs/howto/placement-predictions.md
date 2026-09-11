# Placement previews and predictions

**Problem.** When the player drags a building out, the game shows what it *would*
produce before it is built. Your building shows nothing.

**Solution.** A prediction simulation — a second, lightweight simulation of your
building that runs against hypothetical inputs to compute hypothetical outputs.

## What predictions are for

The prediction system is what powers the placement preview: the little display of "this
cutter would output these two shapes". It is a parallel simulation graph, run on demand
rather than every tick, so the preview can be computed without touching the real
factory.

Every processing building in the game has one. Without it your building is placeable but
the preview is blank, which reads as broken.

## Through Flow

For a building you are adding, it is one stage in the chain:

```csharp
.WithPrediction(new MyPredictionFactoryBuilder(), logger)
```

`DiagonalCutter` supplies `Operation1In1OutPredictionFactoryBuilder`, which is the
shortcut worth knowing: for a machine that takes one item and produces one item via a
shape operation, the generic 1-in-1-out builder does the work — you hand it the
operation and it derives the prediction.

```csharp
IBuildingPredictionFactoryBuilder   // buildings
IIslandPredictionFactoryBuilder     // islands
```

The corresponding extenders are `BuildingPredictionExtender` and
`IslandPredictionExtender`.

## A transport island without one is a hole in the graph

For a machine, a missing prediction means *its own* preview is blank. For anything that
**carries items through** — a belt-like island, a crossing, a custom space path — the cost
is bigger: the prediction graph stops there, so everything *downstream* reads as empty
while real items flow through it perfectly. The usual report is "the shape/fluid readout
flashes Empty even though it's working".

Forwarding is cheap to model. Vanilla's `SpacePathPredictionSimulation` is the whole
pattern — one bundle handed out as both receiver and provider, so whatever it is told
arrives at the other end:

```csharp
public class MyPathPrediction : IItemBundlePredictionSimulation, ISimulation, IUpdatableSimulation
{
    private readonly ItemPredictionBundle<ItemPredictionConverter> Bundle =
        ItemPredictionBundle.Create<ItemPredictionConverter>();

    public int NumItemProviderBundles => 1;
    public int NumItemReceiverBundles => 1;

    public IItemPredictionProviderBundle GetItemProviderBundle(int outputIndex) => Bundle;
    public IItemPredictionReceiverBundle GetItemReceiverBundle(int inputIndex) => Bundle;

    public void Update(Ticks startTicks, Ticks deltaTicks) => Bundle.Update(deltaTicks);
}
```

`ConnectableIslandPredictionSimulation` pairs prediction bundles to connectors by the same
declaration order `ConnectableIslandSimulation` uses for the real ones, so one connector
order serves both — an island with two independent paths returns two bundles and needs no
extra wiring.

### Reaching `WithPrediction` on the island chain

The island fluent interfaces fork, and the obvious route is a dead end.
`WithSimulation` off `IDefinedUnlockableIslandExtender` returns `IAtomicIslandExtender`,
which has no `WithPrediction`. The prediction branch is only reachable through
`IDefinedSimulatableIslandExtender` — and **nothing in the chain returns that interface**.
Cast to it:

```csharp
IAtomicIslandExtender simulated = AtomicIslands.Extend()
    .AllScenarios()
    .WithIsland(island, group)
    .UnlockedAtMilestone(new ByIndexMilestoneSelector(0))
    .WithSimulation(new MySimulationFactory());

((IDefinedSimulatableIslandExtender)simulated)
    .WithDefaultPlacement()
    .InToolbar(slot)
    .WithPrediction(new MyPredictionFactory(), logger)
    .WithoutModules()
    .Build();
```

Safe, because every one of those interfaces is implemented by the same
`AtomicIslandExtender` instance and both `WithDefaultPlacement` overloads are the same
no-op.

## Registering a prediction system directly

For prediction behaviour not tied to one building:

```csharp
using ShapezShifter.Hijack.Predictions;

public class MyPredictionsRewirer : IPredictionSystemsRewirer
{
    public void ModifyPredictionSystems(
        ICollection<ISimulationSystem> simulationSystems,
        PredictionSystemsDependencies dependencies)
    {
        simulationSystems.Add(new MyPredictionSystem(dependencies.ShapeRegistry));
    }
}

GameRewirers.AddRewirer(new MyPredictionsRewirer());
```

Prediction systems are ordinary `ISimulationSystem`s — see
[Add a simulation system](add-simulation-system.md) — registered into a *separate*
collection from the real ones.

`PredictionSystemsDependencies` is a slimmer set than the simulation one:

```csharp
public readonly GameMode Mode;
public readonly IGameResourcesMap ResourcesMap;
public readonly IShapeRegistry ShapeRegistry;
public readonly IShapeIdManager ShapeIdManager;
public readonly IResearchUnlockManager ResearchUnlockManager;
public readonly ILogger Logger;
public readonly ITrainHashCalculatorHeuristic TrainHashHeuristic;
```

No fluid registry, no signal channels — a hint at what predictions are expected to
model.

## What vanilla prediction systems look like

Worth reading before writing one:

| Class | Predicts |
| --- | --- |
| `AtomicBuildingPredictionSimulationSystem` | the generic single-building case |
| `AtomicIslandPredictionSimulationSystem` | the island equivalent |
| `RailOpenOutputsPredictionSimulationSystem` | where rail outputs would go |
| `TrainLauncherCatcherPredictionSimulationSystem` | train launch/catch pairing |
| `TrainCargoLoaderPredictionSubSimulationSystem` | cargo loading |
| `TrashPredictionSimulationSystem` | the trash island — the simplest of the set |

`TrashPredictionSimulationSystem` is the one to start from: minimal, and it shows the
shape of a prediction sub-simulation without train complexity.

## Gotchas

- **Predictions must agree with reality.** If your prediction says one thing and your
  simulation does another, players will report it as a bug in your building — and they
  will be right. Derive both from the same operation where you can, which is exactly
  what the 1-in-1-out builder achieves.
- Predictions run on placement, i.e. during interactive dragging. Keep them cheap; a
  slow prediction shows up as input lag on the placement preview.
- A prediction has no real inputs. It is computing "given an input of X, what comes
  out" — do not reach for live lane state inside one.
- Islands and buildings have separate prediction paths; adding an island with
  simulation means the island variant.

> [!NOTE]
> `DiagonalCutter` is the only sample with a prediction, and it uses the generic
> 1-in-1-out builder rather than a hand-written system. Anything beyond that shape is
> unexplored — read the vanilla systems above and verify against your game version.
