# Read machine state

**Problem.** A `BuildingModel` tells you a machine *exists*. You want to know what it is
*doing* — running, waiting for input, or backed up.

**Solution.** Go from the building to its simulation, then read its lanes.

## Building → simulation

```csharp
if (!map.Simulator.TryFindTileSimulation(building.Tile_G, out ILocalizedTileSimulation localized))
{
    return;   // no simulation here (decoration, foundation, …)
}

if (localized.Simulation is IItemSimulation itemSimulation)
{
    // most machines and belts land here
}
```

`IItemSimulation` is the generic interface: it exposes inputs, outputs, and internal
lanes without you knowing the concrete type. That is what lets one piece of code cover
cutters, painters, stackers, and anything a *different* mod added.

## Running, starved, or blocked

```csharp
public enum MachineStatus { Unknown, Starved, Blocked, Running }

public static MachineStatus Classify(IItemSimulation simulation)
{
    // Output holding an item the next lane refuses → the problem is downstream.
    for (int i = 0; i < simulation.NumItemProviders; i++)
    {
        if (simulation.GetItemProvider(i) is not IItemLane output) continue;
        if (!output.HasItem) continue;

        IBeltItem item = output.GetItem(0);
        if (output.NextLane != null && !output.NextLane.CanAcceptItem(item))
        {
            return MachineStatus.Blocked;
        }
    }

    // Nothing on any input → the problem is upstream.
    for (int i = 0; i < simulation.NumItemReceivers; i++)
    {
        if (simulation.GetItemReceiver(i) is IItemLane input && input.HasItem)
        {
            return MachineStatus.Running;
        }
    }

    return MachineStatus.Starved;
}
```

`Blocked` vs. `Starved` is the distinction players act on: blocked means fix what is
downstream, starved means feed it more. A single percentage number cannot tell them
which.

This costs two property reads per lane and needs no warm-up, so it is cheap enough to
run across thousands of buildings — but a single reading is a snapshot. Sample a few
times a second and keep a rolling ratio before you show anything.

> [!NOTE]
> This classification is derived from the lane contracts, not a vanilla mechanism. The
> game itself measures throughput instead — see below.

## Looking at every internal lane

Inputs and outputs are not the whole machine; a cutter also has a processing lane.
`TraverseLanes` visits all of them:

```csharp
public struct OccupancyCounter : IItemLaneTraverser
{
    public int Total;
    public int Occupied;

    public void Traverse(IItemLane lane)
    {
        Total++;
        if (lane.HasItem) Occupied++;
    }
}

OccupancyCounter counter = new OccupancyCounter();
itemSimulation.TraverseLanes(counter);
```

Use a `struct` traverser — the method is generic specifically so the call inlines and
allocates nothing.

## Measuring actual throughput

This is what the game's own side panel does
(`HUDSidePanelModuleBuildingEfficiency`): hook the output lane, timestamp every item
that arrives, keep a 60-second window, and compare the average interval against the
building's theoretical processing duration.

```csharp
AcceptHookDelegate saved = outputLane.AcceptHook;

outputLane.AcceptHook = delegate(IItemReceiver receiver, ref IBeltItem item, ref Ticks remaining_T)
{
    Ticks now = map.Simulator.GetSimulationTimeFor(localized)
              + map.Simulator.GetSimulationUpdateDeltaTimeFor(localized);

    Timestamps.Enqueue(now - remaining_T);

    saved?.Invoke(receiver, ref item, ref remaining_T);   // ALWAYS chain
};
```

The theoretical rate comes from the definition:

```csharp
if (building.Definition.CustomData.TryGet<IBuildingEfficiencyData>(out var data))
{
    float baseDuration = data.OriginalProcessingDuration;
    int laneCount = data.ProcessingLaneCount;
}
```

Accurate, but it costs a delegate call per item and needs ~60 s of history per machine.
Reasonable for one selected building; expensive for ten thousand.

## Gotchas

- **Always chain `AcceptHook`.** Overwriting it stops the machine doing its own work —
  the cutter that no longer cuts. Save the old delegate, invoke it, and restore it on
  cleanup.
- `BuildingEfficiencyData` is attached to **every** building definition, belts included.
  Its presence does not mean "processing machine" — check the value.
- `building.State` holds the *serialized* state (`…SimulationState`), not the live
  simulation. For "what is happening now", go through the simulator.
- Remove lane hooks when the map unloads, or they pin a dead map alive.
