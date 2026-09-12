# Predictable Belt Filters — design

Belt filters predict what they actually pass, instead of being predicted as plain splitters.

Everything below was read out of `decompiled/` and confirmed in game. The two state-lifetime
traps were not found by reading - they were found by a probe build, after a fix that looked
correct on paper changed nothing. The section at the end says what is still unverified.

## The defect

`BuiltinPredictionSimulationSystems.CreateFlowControlSystems` registers three buildings.
Two are fine and one is not:

| Building | Prediction registered | Correct? |
| --- | --- | --- |
| Belt reader | `ForwardingPredictionSimulation` | yes — it passes items through |
| Pipe gate | `ForwardingPredictionSimulation` | yes |
| **Belt filter** | `SplitterPredictionSimulationFactory(2)` | **no** |

`SplitterPredictionSimulation.Update` pushes one set to every output:

```csharp
PredictedItem predictedItem = Input.PopPrediction();
foreach (var output in Outputs) output.Push(in predictedItem);
```

So both the match and the mismatch belt claim they can carry everything on the line.

## The rule the filter actually follows

`SignalControlledDistributionBehaviour.TryFindNextLane`, which is the behaviour
`BeltFilterSimulation` is constructed with. Lane 0 is `MatchOutputLane`, lane 1 is
`MismatchOutputLane`.

| `CurrentSignal` | Real routing | What this mod predicts |
| --- | --- | --- |
| `BeltItemSignal(V)` | `V` → match, rest → mismatch | match = input ∩ {V}, mismatch = input ∖ {V} |
| `IntegerSignal`, truthy | everything → match | match = input, mismatch = none |
| `IntegerSignal`, falsy | everything → mismatch | match = none, mismatch = input |
| `NullSignal` / `ConflictSignal` | nothing moves at all | *vanilla behaviour* — see below |

Only the `BeltItemSignal` row narrows a set, but that is the row players build.

## Why the wrong prediction is worse than "slightly off"

Two amplifiers, both in the base game, and both fed by sets that are larger than they
need to be:

- **Silent truncation at four.** `PredictedItem` holds exactly four `IItem`s.
  `PredictionCombinationExtensions.CombinePredictions` stops at four, and
  `PredictedItem(List<IItem>)` reads only indices 0–3 — so a fifth possibility is dropped
  with no marker at all.
- **`Degenerated` reads as blank.** `ItemOperationPredictionExtensions.Predict` returns
  `PredictedItem.Degenerated` when the operation fails on **every** candidate it was handed
  (`scopedList.Count == 0 && input != PredictedItem.None`) — note this is *failure on all*,
  not "more than four outcomes". Then both `PassThroughItemPredictionLane.PushPrediction`
  and `ItemPredictionConverter.Update` turn `Degenerated` into `PredictedItem.None`. One
  falsely-populated branch therefore blanks the readout for everything downstream of it.

That second one is why the player-facing complaint is "my belts stopped showing anything"
rather than "the numbers are a bit wrong".

## How a prediction reaches a live wire signal

`PredictionSystemsDependencies` exposes no signal channels — the prediction graph is not
meant to know about wires. It has to be reached through the building's simulation state.

The obvious route is `building.State` on the `BuildingInstance` the prediction system is
handed. **That route does not work, and fails in a way that looks like everything else.**

### Why the prediction graph's own BuildingInstance cannot be trusted

Predictions run over `LazyEventMapLayout`, which queues map edits as `BuildingDescriptor`s
in a `LookupQueue` — a `Dictionary`, so it compares with `BuildingDescriptor.Equals` and
`GetHashCode`. Those compare **definition, transform and configuration, and ignore `State`
entirely.**

`StoreBuildingAdded` cancels an add against a pending remove:

```csharp
BuildingDescriptor item = new BuildingDescriptor(building);
if (BuildingsToRemove.Contains(item)) { BuildingsToRemove.Remove(item); }
else { BuildingsToAdd.Add(item); }
```

When a placement ghost at a tile is removed and the real building at that same tile is added
in one lazy batch, those two descriptors compare **equal** despite pointing at different
state containers. Both cancel. The runner map keeps the *ghost's* `BuildingInstance`, whose
container nothing ever populates, and the prediction side reads an empty container for the
life of that building.

Measured: every filter reporting `NoState` permanently, while the same filters routed items
correctly in the real simulation.

### So the state is published from the real side instead

The real simulator runs on the map layout directly, with no lazy layer, so its
`BuildingInstance`s carry the genuine containers. `FilterStateObserver` is an
`ISpecializedBuildingObserverSimulationSystem` registered through `ISimulationSystemsRewirer`
that records `tile → SimulationStateContainer`; the prediction looks its tile up there.

An observer, not a tenant, because the filter already has a tenant and observers are purely
additive — nothing about how the game simulates a filter changes.

It records the **container**, never the state, because `Simulator.RevealBuilding` runs
observers *before* `OfferBuilding`:

```csharp
RevealBuilding(building, layout);    // observers
OfferBuilding(building, 0, layout);  // tenants — this is where New<T>() happens
```

so any state read at observation time is the one about to be thrown away.

`AtomicBuildingPredictionSimulationSystem` throws the `BuildingInstance` away — it builds
its simulation from an `IFactory<TSimulation>`. `BeltFilterPredictionSystem` therefore subclasses
`AtomicBuildingSimulationSystem<ConnectableBuildingPredictionSimulation>` directly and
overrides `CreateConnectableSimulation(BuildingInstance)`, which is `protected abstract`.
Everything else — tile indexing, connector wiring, offer and termination — is inherited
unchanged, and `ConnectableBuildingPredictionSimulation` still does the connector pairing
exactly as it does for vanilla.

### Three traps on that path

**Never cache the state object — re-resolve it from the container every time.**
`SimulationStateContainer.New<T>()` *replaces* the state rather than filling one in:

```csharp
public T New<T>() { State = new T(); return (T)State; }
```

and `AtomicStatefulBuildingSimulationSystem.CreateConnectableSimulation` calls it every time
the real simulation is built. So a reference captured before that moment is **orphaned** the
instant it happens, and it goes stale in one of two ways:

- the state was never used to build a simulation, so
  `InputConductorState.InputConductor` is still `null` — only the real
  `BeltFilterSimulation`'s constructor assigns it; or
- the state *was* used by a now-superseded simulation, so it holds a perfectly valid
  `SignalConductorInput` that the wire network is no longer connected to. It exists, it
  never throws, and `GetMostRecent()` returns `NullSignal` on it for the life of the
  building.

The second is the one that actually happened, and it is the more dangerous of the two
because every null check passes. Measured on a live save: `stateMisses=0
conductorMisses=0 null=300` on a filter being fed two shapes, beside a working filter on the
same base reading `item=300 latched=[i:--CuCuCu]`.

The symptom is nastily selective, and cost a round trip to find: filters that already existed
when the prediction graph was built work perfectly, while every **newly placed** filter
silently predicts as vanilla forever. Toggling shape predictions off and on "fixes" it,
because that rebuilds the graph after the real state exists — which makes it look like a
graph-staleness problem rather than a dangling reference.

Re-resolving costs one `as` cast per update. Cache nothing.

**`BeltFilterSimulationState.CurrentSignal` is not safe to call.** It dereferences
`InputConductorState.InputConductor`, and that field is only assigned by the
`SignalConductorInput` constructor — i.e. when the *real* simulation is built. Nothing
orders the two systems, so on a freshly placed filter the prediction can run first and NPE.
Reach through `InputConductorState.InputConductor` with a null check instead.

**`SignalBuffer.GetMostRecent()` returns `NullSignal` unless the buffer was written on the
current start tick** (`WasPushedThisStartTick || force`). The prediction graph runs on its
own round-robin (`PredictionUpdateStrategy`), not in step with the signal tick, so reading
it raw makes a correctly-wired filter flicker between "filtered" and "unknown". Hence the
latch: the last concrete signal is remembered.

`GetMostRecent(force: true)` would dodge the freshness test but indexes
`Values[LastSignalTick % ValuesArraySize]` with a plain `%` — unlike `TryPopSignal`, which
uses `FastMath.SafeMod` — so on a buffer that has never been written, `LastSignalTick` is
`SignalTicks.MinValue` and the index can go negative. Not used.

## Deliberate choice: unknown falls back to vanilla

`NullSignal` and `ConflictSignal` both genuinely stop the filter dead, so predicting two
empty lanes would be *correct*. This mod does not do that. An unwired filter is the normal
state of one that was just placed, and blanking both its belts reads as the mod breaking
predictions.

The rule is: **where the filter's behaviour is not known, answer exactly what vanilla would
have answered.** That way the mod can add precision but can never remove information the
base game was already showing.

The cost, stated plainly: a filter whose wire is later *removed* keeps predicting its old
rule until the building is rebuilt, because the latch never clears.

## Replacement, not addition

`PredictionSystemsInterceptor` postfixes
`BuiltinPredictionSimulationSystems.CreateSimulationSystems` and hands each
`IPredictionSystemsRewirer` the assembled list as a mutable `ICollection`. Vanilla's entry
must come **out**: two prediction systems claiming one building would both accept it in
`BuildingIsOffered` and both build a simulation for it.

A system announces its building through
`ISpecializedBuildingTenantSimulationSystem.SpecializedBuildings`, which
`AtomicBuildingSimulationSystem` implements *explicitly* — so it is reachable by casting to
the interface, with no publicizer.

The rewirer only adds its system where it actually removed one. If vanilla's registration
ever stops being discoverable — a game update, or another mod that got there first — the
safe outcome is to leave the prediction graph untouched rather than double-register.

## No publicizer

Unusually for this workspace, this project has no `Krafs.Publicizer` reference. Every
member on the path is already public: the prediction receivers and providers,
`AtomicBuildingSimulationSystem`'s protected hook, `BeltFilterSimulationState.InputConductorState`,
and `SpecializedBuildings` via the interface cast. Adding it for nothing would only add a
build step and the "no members were publicized" warning that costs a session to second-guess.

## Scope

Not addressed, and not addressable from a mod without bigger changes:

- **The cap of four.** `PredictedItem` is a readonly struct with exactly four `IItem` fields,
  passed by value throughout. Nothing widens it.
- **Truncation stays silent.** Past four possibilities you still get four and no marker.
- **`Degenerated` → blank.** `PassThroughItemPredictionLane` and `ItemPredictionConverter`
  are both non-generic, so they *are* hookable without hitting the generic-type trap — but
  `ItemPredictionConverter.Update` runs on every belt lane, and propagating `Degenerated`
  instead of blanking would need the renderer to have something to draw for it. Separate
  job.

Also checked and found **not** to be a problem: every building registered in the real
simulation but absent from the prediction one is signal-layer only — logic gates, constant
signals, displays, buttons, wire transmitters, `Virtual*` processors. None carry belt items,
so having no item prediction is correct. (`Mode.Buildings.ForwardBelt` shows up in that diff
too but is a false positive: the real simulation only reads it as a config source at
`BuiltinSimulationSystems.cs:70`; belts are registered by group in `CreateConveyorSystem`.)

## Verified in game

Confirmed working: a filter wired to a constant shape signal predicts only that shape on its
forward belt and the remainder on its side belt, for filters that already existed *and* for
freshly placed ones. See `Screenshots/PredictableBeltFilters.png`.

Confirmed along the way, from probe output rather than from reading:

- `GetMostRecent()` is reliable for a wired filter - a working one measured `item=300 null=0`
  over 300 prediction passes. The tick-freshness worry that motivated the latch never
  materialised, so the latch is cheap insurance rather than load-bearing.
- The vanilla-fallback path is indistinguishable from "the mod is not installed", which is
  what made the two failed fixes look identical from outside. Any future change here wants a
  probe before a fix, not after.

## Still unverified

- **Provider index to output pairing.** Index 0 is assumed to be the match (forward) output
  and 1 the mismatch (side) one, inherited from `BeltFilterSimulation`'s lane order. The
  screenshot is consistent with that, but it has not been tested against a filter whose
  forward and side belts carry sets that would make a swap obvious in both directions.
- **Mirrored filter variants.** `BeltFilterDefaultInternalVariantMirrored` gets the same
  treatment and the same assumption; a mirrored filter is where a swapped pairing would show
  up first.
- **Conflict signals.** `ConflictSignal` falls back to vanilla by design, but has not been
  exercised - it needs two signal sources fighting over one wire.
