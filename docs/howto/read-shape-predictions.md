# Read what shapes will flow where

**Problem.** You want to know which shape arrives at a belt, a port, or a machine —
without waiting for an item to physically get there, and without composing shape
operations by hand.

**Solution.** The game already knows. Alongside the real simulation it runs a **second
complete simulation whose only job is to work out which shapes can appear where**, and it
is readable from a mod.

This is the *reading* side of the prediction system.
[Placement previews](placement-predictions.md) is the *authoring* side — giving your own
building a prediction so its placement preview works.

## What the prediction graph is

A parallel simulation over the same map layout, built with a different set of systems.
Every processing building appears in it as its `IItemOperation` applied to a set of
possible items:

```csharp
// Processing1In1OutPredictionSimulation.Update
PredictedItem input = Input.PopPrediction();
PredictedItem predictedItem = operation1In1Out.Predict(in input);
Output.Push(in predictedItem);
```

`PredictedItem` is a readonly struct holding **at most four** distinct `IItem`s, with two
extension methods worth knowing:

```csharp
predicted.IsEmpty()        // nothing predicted here
predicted.IsDegenerated()  // more possibilities than the game will track
```

Because the propagation through belts, mergers and splitters is already done, the shape
transfer function of any subgraph can be *read* rather than derived — you never have to
reconstruct a connector graph or compose operations yourself.

## Reaching the simulator

It is a private field on the session, reachable because
[the publicizer](../publicizer.md) opens everything:

```csharp
ISimulator predictions = orchestrator.PredictionSimulator;
```

> [!WARNING]
> It is **null when shape predictions are switched off** in the game's settings, and the
> session throws it away and builds a new one when they are toggled. Read the field fresh
> every time; never cache the simulator.

From there it behaves like any [`ISimulator`](../simulations-and-lanes.md#finding-a-simulation):
`Simulations`, `TryFindChunkSimulation`, `FindAllConnectedSimulations`.

## Only ever read providers

Every prediction simulation implements `IItemPredictionSimulation`:

```csharp
int NumItemReceivers { get; }
int NumItemProviders { get; }
IItemPredictionReceiver GetItemReceiver(int index);
IItemPredictionProvider GetItemProvider(int index);
```

Read the value from a **provider**, which exposes it as a plain property:

```csharp
if (localized.Simulation is IItemPredictionSimulation prediction
    && prediction.NumItemProviders > 0)
{
    PredictedItem predicted = prediction.GetItemProvider(0).PredictedItem;
}
```

> [!WARNING]
> Do not call `ItemPredictionReceiver.PopPrediction()`. It **clears** the stored value as
> it returns it — that is how the graph moves predictions along — so calling it from a mod
> silently corrupts the game's own propagation.

`GetItemProvider` and `GetItemReceiver` have default interface implementations that
**throw `NotImplementedException`** rather than returning null, and a simulation only
overrides the side it actually has. Check the counts first, or catch:

```csharp
private static IItemPredictionProvider ProviderAt(IItemPredictionSimulation prediction, int index)
{
    if (index >= prediction.NumItemProviders) return null;
    try { return prediction.GetItemProvider(index); }
    catch (NotImplementedException) { return null; }
}
```

## Predictions flow downstream from sources

The single most important thing to understand, and the one that costs an implementation if
you miss it:

> [!IMPORTANT]
> A subgraph with no source inside it has **no predictions at all**. Predictions propagate
> forward from extractors and other producers. An isolated copy of a factory — one you
> assembled yourself, or a blueprint expanded somewhere private — predicts nothing,
> because nothing is feeding it.

Concrete shapes appear on a live factory's ports only because that factory is being fed
concrete shapes. So "what does this blueprint output" is not a question the prediction
graph can answer on its own; it answers "what does this blueprint output *given these
inputs*".

Propagation also moves **one simulation per update**, so after any change a deep graph
needs several passes before the far end settles.

## Degenerated is the game's own "give up"

When the set of possibilities at a point exceeds four, the game pushes
`PredictedItem.Degenerated` instead of tracking them.

That is worth reusing rather than reinventing: if you need to decide whether some region of
factory is simple enough to reason about, `IsDegenerated()` at its outputs is a criterion
the game itself computes and stands behind.

## Gotchas found the hard way

**A space port is split in two.** `PredictionSpacePathPortSystem` builds the building half
facing inward and a *separate* buffer simulation two tiles out to carry the hop across
space. So a space **output** port's own simulation has no provider to read — it has only a
receiver. Reach its shapes through whatever feeds it, by indexing every provider in the
region by `provider.Next`:

```csharp
Dictionary<IItemPredictionReceiver, IItemPredictionProvider> feeders = new();
// ...for each provider: if (provider.Next != null) feeders[provider.Next] = provider;

IItemPredictionReceiver receiver = sender.GetItemReceiver(0);
if (feeders.TryGetValue(receiver, out IItemPredictionProvider feeder))
{
    PredictedItem predicted = feeder.PredictedItem;
}
```

**Belt ports and fluid ports use the same classes.** `PredictionSpacePathPortSystem<TInput,
TOutput>` is generic over its connector types, so `SpacePortSenderPredictionSimulation`
serves both. Tell them apart by what the prediction *contains* — a `ShapeItem` versus an
`IFluid` or `FluidPackageItem` — not by the simulation type.

**Do not identify ports by their prediction simulation type.** Every conveyor on a platform
is also a `ForwardingPredictionSimulation`, because `ConveyorPredictionSimulationSystem`
uses one — so testing for that type matches the whole belt network rather than the handful
of ports. Discriminate on the *localized wrapper* instead:

| Wrapper | What it is |
| --- | --- |
| `IConnectablePort` | a genuine port transfer between two port buildings |
| `ConnectableBeltPortSender` | a port building with nothing docked on the far side |
| `ConnectablePathPredictionSimulation` | a belt run — not a port |

See [Ports and Notches](../ports-and-notches.md) for the equivalent taxonomy on the real
simulation graph.

**Off-screen platforms still predict.** Clusters absent from the culler's LOD list are
updated as `SimulationLOD.Lowest`, and `PredictionUpdateStrategy` still schedules those on
a round-robin — so predictions exist for platforms the camera is nowhere near, just
staler ones.

> [!NOTE]
> Derived from the game's own implementation and from a mod that reads the graph, not from
> an official sample. Verify the type names against your game version.
