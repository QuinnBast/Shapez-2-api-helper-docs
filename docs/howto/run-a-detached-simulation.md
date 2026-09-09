# Run a factory the player cannot see

**Problem.** You want to run some factory — a copy of a platform, a blueprint, a layout you
assembled — and observe what it does, without it appearing on the player's map or touching
their save.

**Solution.** Build your own `Simulator` over your own `MapLayout`. Every piece is public,
and the game does exactly this itself: the prediction graph is a second simulator over the
same layout with a different set of systems.

This is a heavyweight tool. Reach for it when you need to *measure* something a running
factory will not tell you — a throughput ceiling, a latency, what a blueprint would do —
rather than to read the factory in front of you, which
[Simulations and Item Lanes](../simulations-and-lanes.md) covers.

## The four pieces

```csharp
// 1. A layout. MapLayout stores; MapLayoutModel is what creates into it.
MapLayout layout = new MapLayout(logger);
MapLayoutModel model = new MapLayoutModel(layout);

// 2. Content. Instances are created, not shared - each gets its own state container.
model.CreateIsland(definition, transform, islandConfiguration);
model.CreateBuilding(definition, transform, buildingConfiguration);

// 3. Systems, built fresh.
ISimulationSystem[] systems = new BuiltinSimulationSystems(
    Ticks.Zero,
    orchestrator.ShapeOperations, orchestrator.Mode,
    orchestrator.ShapeRegistry, orchestrator.ShapeIdManager,
    orchestrator.ResourcesMap, orchestrator.FluidRegistry,
    orchestrator.FluidPackageItemSolver,
    orchestrator.DependencyContainer.Resolve<ISignalChannelRegistry>(),
    orchestrator.Research.UnlockManager,
    logger).CreateSimulationSystems().ToArray();

// 4. A graph and a simulator over the layout.
SimulationGraph graph = new SimulationGraph(Ticks.Zero, updateClustersBeforeModification: true, logger);
Simulator simulator = new Simulator(graph, systems, layout, logger);
```

Two things that are easy to get wrong:

- **`MapLayout` is not the model.** It has no `CreateIsland`; the layout stores and
  `MapLayoutModel` creates into it. The simulator wants the layout itself, so keep both.
- **Systems cannot be shared with the live simulator.** Each holds per-simulator state
  about the buildings offered to it, so build your own set.

> [!NOTE]
> Coordinates are just keys in a private dictionary, so your copies can sit at the same
> coordinates as the originals they were copied from. Nothing collides.

## Driving it

`SynchronousUpdate` advances the simulator by a delta of *simulation* time, independent of
frames:

```csharp
simulator.SynchronousUpdate(delta, config, strategy);
```

Supply your own strategy that never rations work — there is no camera to be far from and no
frame budget to spread updates across, and the level-of-detail rationing the live map does
would only make a measurement harder to interpret:

```csharp
private class UpdateEverything : ISimulationUpdateStrategy
{
    public UpdateNeed EvaluateUpdateNeed(SimulationCluster cluster, SimulationLOD lod,
        Ticks simulationEndTime)
    {
        return simulationEndTime > cluster.ClusterTime ? UpdateNeed.Mandatory : UpdateNeed.Not;
    }
}

// An empty LOD list means every cluster is treated as furthest; the strategy ignores it.
SimulationUpdateConfiguration config =
    new SimulationUpdateConfiguration(new List<SimulationLOD>(), TimeSpan.Zero, 1);
```

`TimeSpan.Zero` for `TimeForPrematureUpdates` means a hard update rather than a
time-budgeted one — a measurement that depends on how much time the scheduler felt like
giving it is not a measurement.

> [!WARNING]
> This runs on the calling thread. Ticking a few hundred simulated seconds of a large
> platform takes long enough to visibly stall a frame, so keep runs bounded and do them in
> response to an explicit action rather than every tick.

Dispose the simulator and the graph when you are done.

## Feeding and draining a detached factory

A copied factory has nothing on the far side of its ports, and the game models that
honestly — which turns out to be exactly the handle you need. See
[Ports and Notches](../ports-and-notches.md) for the full taxonomy; the two that matter
here are:

| Simulation | Meaning | Use it as |
| --- | --- | --- |
| `BeltPortReceiverDisabledSimulation` | receiver whose sender is missing | an **input** — hand it items |
| `BeltPortSenderBlockedSimulation` | sender whose receiver is missing | an **output** — drain its lane |

**To drain**, give the lane somewhere to go. `NextLane` is declared on `IItemProvider` and
is settable, so a sink that accepts everything is enough:

```csharp
private class Drain : IItemReceiver
{
    public int Count;
    public Steps MaxStep_S => LaneConstants.ItemSpacing;   // a full slot's room, always
    public bool CanAcceptItem(IBeltItem item) => true;
    public void HandOverItem(IBeltItem item, Ticks remainingTicks) => Count++;
}

blocked.InputLane.NextLane = new Drain();
```

A **space** output needs one more thing: `SpaceBeltPortSenderSimulation.PathLane` carries a
`PreAcceptHook` that refuses items once the space-side buffer fills, and in a detached world
nobody ever empties that buffer. Relax it:

```csharp
sender.PathLane.PreAcceptHook = _ => true;
sender.PathLane.NextLane = drain;
```

**To feed**, offer items to the lane as fast as it will take them:

```csharp
if (lane.CanAcceptItem(item)) lane.HandOverItem(item, Ticks.Zero);
```

Items are flyweights, so offering the same instance repeatedly is fine.

**Fluid ports are containers, not lanes.** Saturating a tank means keeping it full rather
than handing it things, and draining one means emptying it so it cannot stall the factory:

```csharp
fluidIn.FluidPortReceiver.TryAdd(fluidIn.FluidPortReceiver.RemainingCapacity, fluid);
fluidOut.FluidPortSender.Flush();
```

## Step size matters

Offer items *between* small steps rather than jumping a long delta:

```csharp
Ticks step = Ticks.OneSecond / 30;
for (int i = 0; i < steps; i++) { Offer(); simulator.SynchronousUpdate(step, config, strategy); }
```

A lane only accepts an item once the previous one has moved far enough along. One large
delta lets the belts advance a long way with nothing offered to them, and measures a
starved factory rather than a saturated one.

## Measuring a ceiling, not a snapshot

If you are after what a factory *could* do, two things follow from how factories behave:

- **Discard the fill.** Until the first item has travelled all the way through, the average
  includes a stretch where the answer was necessarily zero. Timing that stretch also gives
  you the factory's latency for free.
- **"Settled" means stopped improving, not "changed little".** A filling factory gains a
  couple of percent every window for a long time, so two adjacent windows can agree closely
  while the rate is still climbing. Require several consecutive windows that fail to beat
  the best seen so far.

For reference, a 954-building platform wired itself into 426 simulations and reached a
steady 4320 items/min after 14 seconds of simulated latency.

## Does it actually work?

Yes — a detached simulator stands up, the systems claim the copied buildings and connect
them, and it ticks. The building-to-simulation ratio is well under 1:1 because a belt run
collapses into a single path simulation.

> [!NOTE]
> Derived from the game's own `GameSessionOrchestrator.CreatePredictionSimulator` and from a
> mod that does this, not from an official sample. Verify the constructor signatures against
> your game version.
