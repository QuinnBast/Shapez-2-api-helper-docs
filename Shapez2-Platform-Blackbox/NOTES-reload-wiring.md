# The reload wiring bug

State as of build `366b3d0a`. One open bug, well characterised. Everything else on the box works.

## Symptom

A blackbox loaded from a save comes back with its **connections intact** but only **some of its
lanes wired**, differently each time the same save file is opened.

- "Select connected" on the platform finds every incoming and outgoing belt.
- The belts draw the orange flow arrow in the correct direction.
- `pbx.box` reports e.g. `24 of 288 lanes attached` with two of four output belts in `out wired`.
- Inputs are affected too, not just outputs: `in fed` lists a subset, `in silent` the rest.
- Deleting and replacing one output belt wires that one belt, permanently.

Consequence: the box can't drain, its pool fills, its intakes refuse, and it looks frozen.

## Cause, as far as it is understood

`SpacePathPortSystem.IslandIsOffered` is what wires an island's item bundles to its neighbours:

```csharp
foreach (var item in connectorData.ConnectorsOfType<TInput>())
{
    var platformOutputPivot = item.Location.ToGlobal(...).CounterpartConnector();
    ... layout.TryGetBuilding(in pivot, out var building) ...   // a port building on the neighbour
}
```

It walks the island's connectors and, for each, looks for a **port building on the platform across
the notch**. So a notch is only wired if the box is offered to the systems *after* the neighbouring
belt's port buildings exist. During a load that ordering is not guaranteed, which explains the
non-determinism: same bytes on disk, different subset wired each time.

The corollary worth keeping: **nothing about what gets written to the save can fix this.** Two
loads of identical data behave differently.

## Ruled out, with evidence

- **Not the saved data.** Serialisation round-trips. The log shows the recipe, signature, blueprint
  length and pool contents coming back on every load.
- **Not connector compatibility.** `IslandConnector.ConnectsTo` checks both ends
  (`a.IsCompatibleConnection(b) || b.IsCompatibleConnection(a)`), so the permissive subclasses in
  `BlackboxConnectors` are enough for connections to resolve in either direction. The arrows and
  "select connected" confirm the map layer is correct.
- **Not the simulation graph.** `ISimulationGraph.Remove` + `Add` ran and changed nothing:
  `0 -> 0 output lanes wired`. The graph holds clusters and update order, not bundle wiring.
- **Not fixable from a tick hook.** `Simulator.TerminateIslandOffer` + `OfferIsland` is the right
  *mechanism*, but the graph reports `IsUpdating` from both a prefix and a postfix on
  `GameSessionOrchestrator.Tick`, so it never runs. From a console command it does run - and still
  did not wire the notches.
- **Not chunk geometry.** Two boxes, one entirely within a chunk and one straddling a chunk corner,
  behave the same.

## Dead ends, and why

- **Re-offering the island mid-session is unsafe.** `SimulationStateContainer.New<T>()` does
  `State = new T()`, and nothing reads the save back into it outside a load - so re-offering costs
  a box its recipe, blueprint and stock unless the state is copied across by hand
  (`BlackboxIslandState.CopyFrom` exists for this, but the whole path is disabled).
- **`canHoldBuildings: true` broke a live save.** It is the flag `NotchConnectors` keys off, and
  with it the game deep-copies the definition per instance and rewrites its connectors. Turning it
  on for a map that already held boxes left a platform that could not be deleted and took the game
  down. **Only try this on a throwaway save with no boxes on it.**

## Untried, and the most promising

Make the box a **buildable platform with player building disabled**
(`canHoldBuildings: true, buildable: false`) and leave the notch tiles solid, so the vanilla
`NotchConnectors` path applies: every empty notch gets a `FakeUniversalConnector`, and once
something is attached the notch's connector is derived from the **port building** the game places.
That is a real saved entity, so a reload reproduces it exactly - which is how ordinary platforms
take any belt or pipe in any direction and never have this bug.

The predicted obstacle: `NotchConnectors` emits `FakeSelectiveConnector` / `FakeUniversalConnector`,
which implement only `IIslandConnector` - not `ISpacePathInputConnector` / `ISpacePathOutputConnector`.
`ConnectableIslandSimulation` wires item bundles from exactly those two typed lookups, so the
bundles may get nothing and the box may not work *even before a reload*. That failure is visible
immediately in `pbx.box` on a freshly placed box (`out wired nothing`, everything `in silent`), so
the experiment costs one minute.

If that obstacle is real, vanilla platforms move items through **port buildings** rather than
island bundles, and matching them means the blackbox becoming a building on a platform rather than
an island-machine. The pool, recipe, measurement and serialisation would all carry over unchanged -
only the IO surface changes.

## The fallback

The vanilla space converter - an island-machine with notch ports, using the same simulation
machinery - declares **one connector per pivot**, inputs and outputs on distinct pivots, fixed in
the definition:

```csharp
list.AddRange(value.IOs.Inputs .Select(p => new EntityIO<...>(p, new SpaceBeltInputConnector())));
list.AddRange(value.IOs.Outputs.Select(p => new EntityIO<...>(p, new SpaceBeltOutputConnector())));
```

Fixed notches make this bug structurally impossible, at the cost of the player having to wire to a
documented layout. Rejected so far because it forces an awkward platform shape, but it is the only
option with no unknowns left in it.

## Workaround for now

Delete and replace one output belt per box after loading. That re-offers the island and wires it.
