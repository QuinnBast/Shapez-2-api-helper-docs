# Design notes

Where the encapsulation idea stands, what was measured rather than assumed, and what to do
next. Written so none of it has to be re-derived.

## The goal

A blueprint is the unit. The player has a blueprint of some factory; placing it as a
**blackbox** gives a single small platform that behaves the same way — items in, items out,
same rates — with the detail hidden, and expanding it gives the blueprint back.

Blueprints are already the game's own idea of a reusable factory part, already saved,
already shared as strings, already placed and copied. Building on them rather than on
arbitrary map selections settles several questions for free:

- **Identity.** Same blueprint, same recipe. That is the key a recipe can be cached under
  and shared across every placement, which is what makes a blackbox cheap.
- **Sharing.** A blackbox travels as its blueprint. No new format.
- **Reversibility.** Collapse is "replace these platforms with a box pointing at blueprint
  B"; expand is "place blueprint B". Both operations already exist and are exact inverses.
- **Boundary.** A blueprint is closed by construction, so its ports are a property of the
  artifact rather than something inferred from where a drag-selection happened to land.

Selecting platforms in the world stays useful, but as the *authoring* step: select, capture
to a blueprint, then blackbox the blueprint.

## What exists today

| | |
|---|---|
| `pbx.analyze` | size, boundary, notches, the platform size a box would need |
| `pbx.ports` | the boundary grouped into notches, with each port's slot and layer |
| `pbx.predict` | the shapes the game expects at every boundary port |
| `pbx.sandbox` | copies the selection into a private world and runs it |
| `pbx.measure` | saturates that copy and reports its ceiling and latency |
| `pbx.recipe` | the same, reached through a blueprint rather than the live map |
| `pbx.capture` / `pbx.export` | save or share the selection as a blueprint |

All read-only as far as the save is concerned. The sandbox commands run a simulation, but on
copies with their own state containers, so they cannot move an item in the player's factory.

## Verified facts

Each of these was checked in the decompiled assemblies or by running code, not inferred.

| Question | Answer | Where |
|---|---|---|
| Can platforms hide at layer −999? | **No.** `GameMode.MinIslandLayer` is hard-coded to `0`; the max is `0 + IslandLayerUnlocks.Count`, and placement is validated against it | `GameMode`, `BaseMapInteractionMode` |
| Does distance cost load time? | **No.** The map is entirely sparse - dictionaries keyed by id and chunk - so nothing exists between islands | `Map.IslandsById`, `ChunkBuildingsMap` |
| Are far-off regions generated? | **No.** Superchunks are created on demand | `MapModel.GetOrCreateSuperChunkAt_SC` |
| Does the save grow with distance? | **No.** The serializer walks the islands that exist | `MapSerializer` |
| Does hiding platforms shrink a save? | **No.** Save size tracks building count, wherever the buildings are | same as above |
| Is simulation cost flat in building count? | **No** - see [the LOD correction](#the-lod-correction) | `ProcessingUpdateStrategy`, `SimulationGraph.Update` |
| Can a machine's transformation be read? | **Yes**, as `IItemOperation`; the 1In1Out / 1In2Out / 2In1Out / 2In2Out variants each expose `TryExecute` | `Game.Content.Features` |
| Does the game already compute which shapes appear where? | **Yes** - see [the prediction graph](#the-prediction-graph-does-the-shape-half-already) | `Game.Content.Features.Predictions` |
| Can a second simulator be built and ticked independently? | **Yes.** `Simulator` takes `(ISimulationGraph, IEnumerable<ISimulationSystem>, IReadOnlyMapLayout, ILogger)` and exposes `SynchronousUpdate(deltaTicks, config, strategy)`. The game builds a second one itself for predictions | `Simulator` ctor, `GameSessionOrchestrator.CreatePredictionSimulator` |
| Capturing a selection? | `IslandBlueprint.FromSelection(selection, dataSerializers)` - the same call the game's copy button makes | `HUDIslandBlueprintPlacement` |
| Saving and sharing? | `IBlueprintLibrary.TrySaveEntry(...)`, `IBlueprintExporter.Export(...)`; both bound in the session's `DependencyContainer` | `GameSessionOrchestrator` ~1302-1306 |
| Storing mod data in a save? | ShapezShifter's `AttachSaveData<T>` | Shifter `ModSaveDataExtensions` |
| Does a second simulator actually run? | **Yes.** A copy of 954 buildings wired itself into 426 simulations and ticked | `pbx.sandbox` |
| Can a saturated copy be measured? | **Yes.** A rotator block reads 4320/min with 14s of latency; a painter block 2160/min with 10s | `pbx.measure` |
| Can a blueprint be expanded into a sandbox? | **Yes**, and faithfully - identical building and simulation counts to copying the live platforms | `pbx.recipe` |
| How many ports does a notch hold? | **Twelve.** `NotchDefinition.NotchTileCount` is 4, on each of 3 building layers | `NotchDefinition` |
| Does a normal platform have island connectors? | **No.** It docks by notches, and its ports are belt-port *buildings* | `BiggerPlatforms` sample |

## The prediction graph does the shape half already

The single most useful thing found so far. Alongside the real simulation the game runs a
**second complete simulation whose only job is to work out which shapes can appear where**,
and it is reachable from a mod.

`PredictedItem` is a set of at most four distinct `IItem`s. Each processing building appears
in the prediction graph as its `IItemOperation` applied to that set:

```csharp
// Processing1In1OutPredictionSimulation.Update
PredictedItem input = Input.PopPrediction();
PredictedItem predictedItem = operation1In1Out.Predict(in input);
Output.Push(in predictedItem);
```

So the shape transfer function of any subgraph can be **read** rather than derived. Two
design questions this closes outright:

- **Deriving the flow order from a blueprint** is unnecessary. The propagation through
  belts, merges and splits is already done.
- **Composing operations by hand** is unnecessary for the same reason.

And when the possibility set overflows four, the game pushes `PredictedItem.Degenerated`.
That is the refusal criterion, decided by the game rather than by a heuristic this mod
invents: **any boundary output that reads degenerated cannot be reduced to a recipe.**

Details that matter when reading it:

- The simulator is a private field, `GameSessionOrchestrator.PredictionSimulator`, reachable
  because the project publicizes everything. It is **null when shape predictions are off** in
  settings, and is recreated when they are turned back on, so read it fresh each time.
- `ItemPredictionProvider.PredictedItem` is a safe property read.
  `ItemPredictionReceiver.PopPrediction()` **clears** the stored value - calling it from a
  mod corrupts the graph. Only ever read providers.
- A space port is split in two in the prediction graph: the building half faces inward, and a
  separate buffer simulation two tiles out carries the link across space. So an output port's
  own simulation has no provider to read - reach its shapes through whatever feeds it.
- Belt ports and fluid ports use the *same* prediction simulation classes, generic over their
  connector types. They are told apart by the predicted item, not by the simulation type.
- Clusters absent from the culler's LOD list are updated as `SimulationLOD.Lowest`, which
  `PredictionUpdateStrategy` still schedules on a round-robin. Off-screen platforms should
  therefore have predictions, just staler ones. Reasoned from the code, not yet observed.

## The LOD correction

The earlier note here said simulation cost tracks building count wherever the buildings are.
That is wrong, and the correction is worth keeping because it changes *why* relocation is a
dead end rather than whether it is.

Simulation LOD is driven by camera distance (`MapCuller` → `LODRenderConfig.ComputeSimulationLOD`),
and `ProcessingUpdateStrategy` maps LOD to a required update interval through
`RequiredUpdateTicksByLOD`, built as `MaxSimulationUpdateDelta >> (n - i - 1)`. Distant
clusters update less often, with a bigger delta each time. Because `BeltLane.Update(Ticks
deltaTicks)` advances a single item's progress in one step, a 60-tick update costs about the
same as a 1-tick one — so those coarse updates really are cheaper, not just rarer.

But clusters that are not in the cull result at all get `SimulationLOD.Lowest` already
(`SimulationGraph.Update`). **Anything off-screen is already getting the maximum discount.**
Moving platforms somewhere distant therefore buys nothing that not looking at them did not
already buy, which kills the relocation approach more firmly than the original reasoning did.

It also sets the bar for a blackbox: the win has to come from updating *less often than the
lowest LOD does*, or not at all while nothing at the boundary changes.

## Approaches considered

### 1. Park the real platforms out of view

Capture, move somewhere distant, leave a stand-in that tunnels items to and from it.
Behaviour is exact because nothing is approximated. But the buildings still exist, so the
save is the same size, and per [the LOD correction](#the-lod-correction) the CPU discount was
already free. The item tunnelling also has no vanilla equivalent to copy.

Verdict: solves the wrong problem, and the expensive part is the tunnelling.

### 2. One shared instance, many proxies routing items through it

One real rotator block serving fifty placed boxes. Breaks on throughput: fifty boxes sharing
one instance each get a fiftieth of the expected rate, and items from fifty callers have to
be interleaved through one pipeline and returned to the right caller in the right order with
the right latency.

Verdict: no.

### 3. Share the recipe, not the machine — **recommended**

Split immutable from mutable:

- **Shared, one copy per blueprint:** the transformation, the rate ceiling, the port layout.
- **Per placed box:** the items in flight and their progress. Bytes, not buildings.

Each box runs the full transformation at its own full rate, so fifty boxes give fifty times
the throughput, correctly, at one simulation each. Save data becomes one blueprint reference
plus a few bytes per box, which scales with boxes rather than buildings.

The blueprint framing is what gives this a natural sharing key: the recipe is a pure function
of the blueprint, so it is derived once and cached against the blueprint's identity.

## Rates

The shape half is read off the prediction graph. The timing half is not — predictions carry
no rate at all. A box has to answer, on each update: may this output hand an item out, and
may this input accept one. That needs five things.

| Parameter | Meaning | Where it comes from |
|---|---|---|
| shape map | which shapes out for which in | prediction graph, read directly |
| **R** | throughput ceiling per output | saturation run |
| **ratios** | input consumed per unit of output | runs at reduced input |
| **L** | latency, in to out | one tagged item through an empty box |
| **B** | buffer depth | fill with the outputs blocked, count what goes in |

**Ratios are the part that is easy to get wrong.** A box characterised only at saturation is
wrong at every other operating point. A single-input chain is just `out = min(in, R)`. A
multi-input block needs `out = min(R, min_i(in_i / c_i))` — if a stacker's second stream
stops, its output stops. That is a linear recipe, which means a two-stream stacker belongs in
the tier that *works*, not the tier that gets refused.

Rates also quantise: a `BeltLane` holds one item with a progress in `Steps`, spaced
`LaneConstants.ItemSpacing` apart, so a box should emit on a schedule rather than
fractionally. An integer token bucket keeps it deterministic, which the simulation requires.

## The sandbox simulator

The mechanism that ties the rest together, and the reason the blueprint framing helps.

`Simulator` has a public constructor over interfaces, and the game already builds a second
one for predictions over the same layout:

```csharp
// GameSessionOrchestrator.CreatePredictionSimulator
predictionGraph = new SimulationGraph(Ticks.Zero, updateClustersBeforeModification: false, Logger);
predictionSimulator = new Simulator(predictionGraph, systems, map, Logger);
```

`IReadOnlyMapLayout` is a small interface — six events, two collections, two counts, two
lookups. So a **private world containing one blueprint and nothing else** is constructible,
and `SynchronousUpdate` ticks it on demand by whatever delta is wanted.

Its job is **characterisation, not runtime**:

1. Place the blueprint into a private layout, saturate every input, tick to steady state,
   measure — that gives `R` and the ratios.
2. One item through an empty box gives `L`; filling it with outputs blocked gives `B`.
3. Read `PredictedItem` at the boundary for the shape map.
4. Throw the sandbox away. Placed boxes run the recipe.

Run the real simulation *once, offline, to learn the recipe* — then run the recipe. That is
what makes the copy-and-simulate idea pay: it never runs during play, so it never has to be
cheaper than the thing it replaces.

Two things it unlocks beyond the measurements:

- **The existing code works on blueprints unchanged.** Port classification,
  `FindAllConnectedSimulations`, even the throughput meter from the Platform Efficiency
  Overlay all need live simulations, and the sandbox supplies them. A blueprint becomes a
  tiny private map, and everything already written applies to it.
- **Fit-and-verify instead of static tiering.** Derive a candidate recipe, then run the
  sandbox against it at several input mixes and check they agree. Collapse only when they do.
  That is an empirical tier test rather than a hand-classification of building types, and it
  doubles as a permanent regression check.

## The shape half is a function, not a constant

The design used to talk about "the shapes a blueprint emits" as though that were a property
of the blueprint. It is not, and finding out cost a wasted implementation.

A blueprint expanded into a sandbox on its own has **no predicted inputs at all**.
Predictions flow downstream from sources, and an isolated block has none — so standing a
prediction graph over the sandbox reads an answer that cannot exist. Concrete shapes only
appeared for a live selection because the live factory was being fed concrete shapes.

So a recipe is `f(inputs)`, and the sandbox is fed a **probe** instead: take the shape the
block is actually being fed, put it in, and record what arrives at the drains. The output
half is then observed rather than predicted, which is strictly better evidence.

This is also the answer to what happens when a placed box's input changes — and the two
halves of a recipe turn out to behave completely differently:

| | cost to recompute | how often it changes |
|---|---|---|
| **shapes** | nearly free — prediction is pure function application, no simulation time | every time the input changes |
| **rates** | a saturation run, about a second of wall clock | rarely; mostly invariant to which shape flows |

That asymmetry is what makes it tractable. Recompute shapes on every input change, on the
same cadence the game already predicts for real factories. Measure rates once and cache them
under `(blueprint, input shape set)` rather than blueprint alone — because rate *can* depend
on shape: a cutter fed halves rather than wholes, or filters routing different shapes down
different paths and hitting different bottlenecks.

What the box does while re-measuring answers itself. A real factory that gets rewired does
not switch output instantly either; it takes the pipeline latency to flush. A box that stalls
for its measured latency after an input change is **more** faithful than one that switches
instantly, so the measurement cost hides inside behaviour that should happen anyway.

## Notches are the unit of mapping

A notch is one side of one chunk: `NotchDefinition.NotchTileCount` is 4 tiles, repeated on
each of 3 building layers, so **one notch carries up to twelve ports**. A platform of w by h
chunks has `2(w+h)` of them.

The notch, not the individual port, is what a box has to preserve. One notch on the original
becomes one notch on the box, with every port keeping its slot index and layer — so the box
is indistinguishable from outside, and the belts already running into it still line up.

Mapping at any finer grain would mean inventing a correspondence the player never gave:
splitting one connection across two notches, or deciding which of four slots a belt should
land in. At notch granularity **the player's own boundary is the specification**, and nothing
is guessed.

This settles three questions that looked hard:

- **Input versus output** is not a property of the platform. It is which port *building* sits
  at that tile — sender or receiver — exactly as on any other platform.
- **What size box** follows from the notch count, not the port count. The painter block below
  has 36 ports on 3 notches and fits a 1x1.
- **Ports in the wrong place** stops being baked in, because ports are placed buildings. The
  box puts each one on the same side of the boundary the original had it on, and moving one
  later is an ordinary edit rather than a special feature.

## Geometry never gates compaction

An earlier version of this document treated a wide boundary as making a selection
un-boxable. That is wrong. Since a w by h platform offers `2(w+h)` notches, there is always a
platform with room — the notch count only decides how *big* the box is.

Size is linear in notch count, not quadratic, because notch capacity grows twice as fast as
area does. A 1 by k strip gives `2(k+1)` notches for k chunks, so twelve notches fit a 1x5
(five chunks) or a 3x3 (nine). The mod grows the short side first, because a square reads
better on the map than a long sliver and the chunk difference is small.

**The real gate is whether the block reduces to a recipe at all** — the degenerate-prediction
criterion — which is a completely separate question from how wide its boundary is.

Map area was never the point anyway. Save size and simulation cost both track building count,
so a 3x3 box standing in for 414 buildings is very nearly as good as a 1x1 doing it. The
platform is empty either way; it is the buildings that stop existing.

## What the box should be

**A normal platform whose ports are real belt-port buildings** — not a special island with
connectors baked into its definition.

The `BiggerPlatforms` sample settles this: an ordinary buildable platform passes
`Array.Empty<EntityIO<LocalChunkPivot, IIslandConnector>>()` as its connector data. It docks
by notches, and its ports are buildings the player places. Only a special non-buildable
island — like the `SandboxIslands` sample's FluidTrash, which swallows space pipes directly —
declares island connectors of its own.

Taking the building route means the box connects to the rest of the factory identically to
any platform, uses the game's own port buildings, and needs no family of definitions per port
arrangement. `AtomicIslands.Extend()...WithIsland(...).WithSimulation(...)` is about 120 lines
in the sample.

## Ports have to be classified structurally

Not by walking the connection graph, which cost a real bug. A space belt port's graph
neighbours are **all on its own platform** — the hop across space is carried by a separate
path simulation — so asking "does anything it connects to lie outside the selection" answers
no, and the most external thing on the platform looks internal.

The shapes the game can leave a port in, all of which are boundary ports:

| Simulation | Meaning |
|---|---|
| space belt / fluid sender or receiver | reaches across space, so always external |
| `BeltPortSenderBlockedSimulation`, `BeltPortReceiverDisabledSimulation`, fluid equivalents | no counterpart on the far side; external by definition |
| `BeltPortTransferSimulation`, `FluidPortTransferSimulation` | spans both platforms; external iff the selection owns exactly one end |

Two details that each cost a debugging round:

- A transfer simulation occupies **two** chunks, and `GetOccupiedChunk(0)` is the sending
  side. Checking only the first chunk makes every inbound docked port invisible.
- Ownership has to be established **before** classifying. The simulator holds every port on
  the map, so a classifier that skips that test reports the whole map's boundary — 384 ports
  across 36 notches for a one-chunk platform, in the case that caught it.

## The honest limitation

A recipe box is exact **in steady state only**. During ramp-up, and after a stall clears, it
is an approximation: latency and buffer depth model the pipeline but not its internal
transient dynamics. That is a decision to make deliberately and tell the player about, not a
bug to hide later.

For blueprints that cannot reduce to a recipe the cheap fallback is a **rendering-only
collapse** — the platforms stay real, wired and ticking, and are merely drawn as one box. No
fidelity risk, no CPU or save win, but most of the visual benefit for a fraction of the work.

## Worked example

A painter platform, which exercises every path: shape in, fluid in, shape out.

```
1 platform, 1 chunks, 414 buildings
crossing the boundary: 24 in, 12 out (24 internal ports would be hidden)
across 3 notches          -> North 12 ItemIn, South 12 ItemOut, West 12 FluidIn
smallest platform: 1x1

12 x --CuCuCu  +  12 x fluid Red   ->   12 x --CrCrCr
2160/min, 10s latency
```

414 buildings replaced by one recipe, on the platform it already occupies. The boundary
figures are produced twice by independent code — structurally from the real graph, and from
the prediction graph — and their agreeing is what makes them trustworthy.

## Next steps

1. **Consumption ratios.** The ceiling is measured, but not what the block needs in order to
   reach it. If the painter's paint stops, its output must stop, and nothing in the recipe
   says so. Measurable in the sandbox by running at reduced input and watching the response.
2. **The box entity.** A platform definition plus a simulation implementing the recipe as a
   delay line and a rate limiter. Hard-code a rate first and prove a box can be placed and
   simulated before wiring the real recipe in.
3. **Port layout from the notch grouping**, which `pbx.ports` already produces.
4. **The placement toggle** — `c` while placing a blueprint to place it compacted.
5. **Save data**, so a placed box survives a reload.

## Stretch goal: boxing for UPS, not for tidiness

Everything above treats a box as something the *player* asks for - pick a blueprint, get a
small platform. The same machinery aimed at performance instead is a different product, and
a bigger one.

The observation is already in [the LOD correction](#the-lod-correction): simulation cost is
not flat in building count. So a factory has a size ceiling, and that ceiling - not shapes,
not space - is what stops late-game saves. A box replaces N simulated buildings with a delay
line and a rate limiter, which is exactly the trade that ceiling needs.

Automatic boxing would watch for subgraphs that have settled - stable input rates, stable
output rates, nothing changing - swap in the analytic model, and restore the real simulation
the moment the assumption breaks. The player never asks for it and ideally never notices,
beyond the factory continuing to run at size.

What makes it hard is not the boxing, which is the same recipe work as the manual case. It is
**invalidation**: knowing when a box has started lying. A box is only correct while its inputs
stay within the envelope it was measured at, so the triggers to work out are at least:

- an input rate moving outside the measured envelope, in either direction
- a building placed, removed or reconfigured anywhere inside the boxed region
- a research unlock changing any machine's ceiling, which rescales every recipe at once
- a downstream blockage, since a rate limiter that ignores backpressure would invent items
- a shape reaching a boxed region that was not in the measured input set

Get invalidation wrong and the factory quietly produces wrong numbers, which is far worse
than being slow. So the honest ordering is: manual boxing first, and only once recipes are
trusted does automatic boxing become a question of when to apply them rather than whether
they are right.

Worth noting it inverts the priority on one open question below. If boxing is a performance
feature, a box surviving a reload stops being a convenience and becomes the whole point,
since the saves that need it are exactly the ones that take a long time to load.

## Open questions

- Whether a mod can place belt-port buildings onto its own island definition and have the
  port systems claim them. The sandbox proves the systems claim copied buildings, so it
  should hold, but the samples do not cover this case.
- Heterogeneous inputs. A block fed two different shapes cannot be probed with one of them
  without inventing which port wants which. Solving it needs port identity — the same mapping
  the box's own ports need — so it is worth solving once, for both.
- Whether a space belt joining two selected platforms should count as internal. Today it
  counts as external, which costs a spare notch and never loses a connection; proving
  otherwise means following the space path.
- What a box does while its rate is being re-measured, beyond stalling for its latency.
- Whether the four icons a platform shows in space view can be driven by the mod, which is
  the visual payoff of collapsing.
- Whether a blueprint's own identity can key a recipe cache across sessions, and where that
  cache lives in the save.
