# Design notes

Where the encapsulation idea stands, what was measured rather than assumed, and what to do
next. Written so none of it has to be re-derived.

## The goal

The player selects platforms and gets back a single small platform that behaves the same
way — items in, items out, same rates — with the detail hidden. Ideally it also makes big
saves smaller, and ideally the result can be reused like a blueprint.

## What exists today

`pbx.analyze`, `pbx.ports`, `pbx.capture`, `pbx.export`. Reading a selection, working out
which ports cross its boundary, and saving it as a blueprint. Read-only.

The boundary count is the gate on everything else: a selection with three external
connections is a part worth boxing, one with forty is just a region of factory and no small
platform can stand in for it.

## Verified facts

Each of these was checked in the decompiled assemblies or by running code, not inferred.

| Question | Answer | Where |
|---|---|---|
| Can platforms hide at layer −999? | **No.** `GameMode.MinIslandLayer` is hard-coded to `0`; the max is `0 + IslandLayerUnlocks.Count`, and placement is validated against it | `GameMode`, `BaseMapInteractionMode` |
| Does distance cost load time? | **No.** The map is entirely sparse - dictionaries keyed by id and chunk - so nothing exists between islands | `Map.IslandsById`, `ChunkBuildingsMap`, `IslandChunksMap` |
| Are far-off regions generated? | **No.** Superchunks are created on demand | `MapModel.GetOrCreateSuperChunkAt_SC` |
| Does the save grow with distance? | **No.** The serializer walks the islands that exist | `MapSerializer`, `foreach (IslandModel island in Map.Islands)` |
| Does hiding platforms shrink a save? | **No.** Save size and simulation cost track building count, wherever the buildings are | same as above |
| Can a machine's transformation be read? | **Yes**, as `IItemOperation`; `IItemOperation1In1Out.TryExecute(input, out output)` is the real thing | `Game.Content.Features` |
| Where is `Operation` exposed? | On the **prediction** simulations only - 1In1Out, 1In2Out, 2In1Out, 2In2Out - not the live ones. So harvest at capture time, while the buildings still exist | `IItemOperationSimulation` |
| Capturing a selection? | `IslandBlueprint.FromSelection(selection, dataSerializers)` - the same call the game's copy button makes | `HUDIslandBlueprintPlacement` |
| Saving and sharing? | `IBlueprintLibrary.TrySaveEntry(...)`, `IBlueprintExporter.Export(...)`; both bound in the session's `DependencyContainer` | `GameSessionOrchestrator` ~1302-1306 |
| Storing mod data in a save? | ShapezShifter's `AttachSaveData<T>` | Shifter `ModSaveDataExtensions` |

## Approaches considered

### 1. Park the real platforms out of view

Capture the selection, move it somewhere distant, leave a stand-in that tunnels items to
and from it.

Behaviour is exact because nothing is approximated, and distance is genuinely free. But it
**saves nothing** - the buildings still exist, so the save is the same size and the
simulator still ticks all of them. It tidies the map and nothing else. The item tunnelling
also has no vanilla equivalent to copy.

Verdict: solves the wrong problem, and the expensive part is the tunnelling.

### 2. One shared instance, many proxies routing items through it

Tempting - one real rotator block serving fifty placed boxes - but it breaks on throughput.
Fifty boxes sharing one instance each get a fiftieth of the expected rate, and items from
fifty callers have to be interleaved through one pipeline and returned to the right caller
in the right order with the right latency. The sharing fights the semantics.

Verdict: no.

### 3. Share the recipe, not the machine — **recommended**

Split immutable from mutable:

- **Shared, one copy per distinct blueprint:** the transformation, the rate ceiling, the
  port layout.
- **Per box:** the item in flight and its progress. Bytes, not buildings.

Each box then runs the full transformation at its own full rate, so fifty boxes give fifty
times the throughput, correctly, at one simulation each. Save data becomes one blueprint
blob plus a few bytes per placed box - which is the size reduction the whole idea was for,
and it scales with boxes rather than buildings.

## How accurate the recipe can be

| Selection | Recipe from | Accuracy |
|---|---|---|
| chain of single-input machines | composing each `IItemOperation` | **exact** - same shape out as the real chain, forever |
| multi-input with steady ratios | measured boundary rates | good in steady state |
| output depends on accumulated state, or on the mix of arriving shapes | — | refuse; keep as real platforms |

The exact tier is the valuable one and it covers the obvious cases - a rotator block, a
cutter-and-discard chain, a painter line. Function composition holds as long as each item's
fate depends only on that item.

Measuring the middle tier is already solved: the Platform Efficiency Overlay counts real
throughput per port, so the ratios can be taken from a running factory rather than guessed.

## Next step

Before building the box, verify the assumption everything rests on. A `pbx.recipe` command
that:

1. walks the selection's buildings,
2. finds each one's prediction simulation and reads its `IItemOperation`,
3. orders them along the flow using the blueprint's connector graph,
4. composes them, and
5. **prints the derived transformation** - for example "any shape in → rotated 180°, capped
   at 120/min".

Run it on a real rotator block, then on something deliberately awkward like a stacker block
fed by two streams. That immediately shows which tier a selection falls into, and whether
the composition agrees with what the factory is observably doing.

If the composition comes out wrong on a rotator, nothing built on top of it will work - so
this is the cheap experiment worth doing first.

## Open questions

- How to reach a building's prediction simulation from the map. The prediction graph mirrors
  the real one, but the route from `BuildingModel` to its prediction sim is not yet traced.
  `PredictionSystemsInterceptor` and `PredictionUpdateStrategy` are the places to look.
- Deriving flow order from an `IslandBlueprint`. The entries carry definitions, positions
  and rotations, so the connector graph should be reconstructible, but it has not been
  tried.
- What the box should be: a custom building on a small platform, or a custom island
  definition. The island route matches "1x1 platform" but islands need layout data, and the
  sample's own layout code carries a `// TODO: Create fluent API for this`.
- Whether collapsing should be reversible in place (expand → real platforms → collapse) and
  what that means for the boundary connections.
