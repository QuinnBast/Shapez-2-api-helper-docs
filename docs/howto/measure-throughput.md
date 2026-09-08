# Measure what a factory is actually moving

The game knows what a machine *could* do. Working out what it *is* doing means counting
items as they move, and that turns out to be the interesting part: where to count, how to
turn counts into a rate, and what to divide by so a percentage means something.

Everything here was learned building a throughput overlay over a 43-hour save, and the
mistakes are called out where they cost real time.

## Count on `PostAcceptHook`, not `AcceptHook`

Every lane exposes three hooks through [`IHookableItemReceiver`](../simulations-and-lanes.md#lane-hooks).
For counting, `PostAcceptHook` is the one you want:

```csharp
// Fires once per item that lands on the lane.
lane.PostAcceptHook = (PostAcceptHookDelegate)Delegate.Combine(
    lane.PostAcceptHook,
    new PostAcceptHookDelegate(OnItemAccepted));
```

It is a plain multicast delegate. The game combines onto it in a few places
(`CargoPackageTrack`, `PathMergerSimulation`) and so can you, without disturbing anything
already listening. Remove yours with `Delegate.Remove` and the same method group.

`AcceptHook` is a trap for this purpose. It is a single delegate that callers **replace**,
saving the previous value and restoring it on dispose — which is exactly what the vanilla
building-efficiency panel does to whatever building you select. Two mods, or one mod and
the panel, taking turns at save-and-restore will drop each other's hooks depending on the
order they happen to detach. Use `AcceptHook` only when you need to *modify* the item in
flight, which is what it is designed for.

## Not everything has a lane

Three shapes of item-moving simulation, and you need all three or your coverage quietly
has holes:

| Interface | What it is | How to reach the lanes |
|---|---|---|
| `IItemSimulation` | ordinary machines, belts, ports | `GetItemReceiver(i)` / `GetItemProvider(i)` |
| `IItemBundleSimulation` | space belts and space pipes | `TraverseLanes(traverser)` over an `ItemLaneBundle` |
| neither | space *fluid* ports | no lane at all — see [fluids](#fluids-are-packaged-not-poured) |

A space belt is a **bundle**: a dozen or so parallel `FastBeltPathLane`s, four per building
layer. It does not implement `IItemSimulation`, so code that only handles that interface
skips space belts entirely.

> [!NOTE]
> `GetItemReceiver` and `GetItemProvider` are default interface methods that **throw
> `NotImplementedException`** when the concrete type does not override them. Catching that
> per instance is very expensive — a large base has tens of thousands of copies of the same
> few types. Cache the answer per `Type` and each type throws at most once.

## A belt path is one simulation, not one per tile

A run of belt is a single `ConveyorPathSimulation` holding one `BeltPathLane` whose `Slots`
cover the whole path. This is good news for scale: a base with a million belt tiles has
only as many belt simulations as it has *paths*.

It also means a belt hands out **the same lane object** as both its receiver and its
provider:

```csharp
// ConveyorPathSimulation
public IItemReceiver GetItemReceiver(int index) => Lane;
public IItemProvider GetItemProvider(int index) => Lane;
```

That is a useful structural signal. If any metered lane is reference-equal to an input
lane, the simulation *transports* rather than *transforms* — which decides what its
ceiling should be, below.

## Turning counts into a rate

Count into buckets of simulation time, not wall-clock time — `ISimulator.SimulationTime`
stays correct across pausing and the speed controls, which `Time.deltaTime` does not.

The mistake worth avoiding: a **fixed** window. A busy belt delivers hundreds of items in
five seconds and wants a short window so the display reacts. A machine producing one item
every twenty seconds, measured over twelve, gives a window that is usually empty and
occasionally holds one — so the rate flickers between zero and double, and anything
downstream of that number (colours, percentages) flickers with it.

Grow the window backwards until it holds enough arrivals to divide by:

```csharp
// Walk back from the newest completed bucket until there are enough samples.
for (int i = 1; i < BucketCount; i++)
{
    int index = (Head - i + BucketCount) % BucketCount;
    bucketsUsed++;
    total += Buckets[index];
    if (total >= MinSamples) break;
}

return total * 60f / (bucketsUsed * BucketSeconds);
```

Fast flows resolve in one bucket; slow ones extend to the full window. Add a rule that
turns a long silence into zero, or a stopped machine keeps reporting its old average until
the samples age out. Slow and stopped genuinely cannot be told apart without waiting.

This is the same statistic the game's own gauge computes — it stores a timestamp per item
and averages the gaps — just bucketed instead of one entry per item.

## What to divide by

A percentage is only as good as its denominator, and the right denominator depends on what
the thing does.

**Transport** — belts, space paths, ports — is limited by how fast its lane carries items.
Read that from the live lane and you also pick up any speed research:

```csharp
Ticks perItem = LaneConstants.ItemSpacing / path.StepsPerTick_S;   // BeltPathLane
float maxPerMinute = 60f / perItem.FloatSeconds;
```

**Machines** are limited by how long they take to process one item, which only the
definition knows:

```csharp
building.Definition.CustomData.TryGet(out IBuildingEfficiencyData efficiency);
float maxPerMinute = 60f / efficiency.OriginalProcessingDuration * efficiency.ProcessingLaneCount;
```

Getting this backwards is not a subtle error. A machine's output lane is just a belt, so
measuring a machine against lane speed rates a perfectly busy machine at a few percent of
a belt it was never going to fill. Measuring a belt against a building definition drags in
`ProcessingLaneCount` and ignores belt research, which can halve a saturated belt's
reading.

> [!WARNING]
> `OriginalProcessingDuration` is the **base** duration. Speed research makes the real
> ceiling higher, so a fully fed upgraded machine measures above 100% and clamps. Correcting
> it needs the research multiplier for that building's `ResearchSpeedId`, via
> `ISimulationSpeedsProvider` — see [speed upgrades](speed-upgrades-and-buffs.md).

## Throughput alone cannot find a bottleneck

Two machines both running at 16% need opposite responses. One is fed and cannot keep up —
the constraint is it or something downstream. The other is not being given enough to do —
the constraint is upstream. Throughput is identical; only the supply side differs.

Measure it separately as **saturation**: how full the lanes feeding the thing are.

```csharp
static float Occupancy(IItemLane lane)
{
    int capacity = lane switch
    {
        BeltPathLane path => path.Slots.Count,
        FastBeltPathLane fast => fast.ItemCapacity,
        SingleItemLane _ => 1,
        _ => 0
    };

    return capacity > 0 ? math.saturate(lane.ItemCount / (float)capacity) : (lane.HasItem ? 1f : 0f);
}
```

Smooth it, and low throughput plus high saturation means "look here or downstream", while
low throughput plus low saturation means "look upstream". For a fluid port, its tank
`Level` is the same signal.

## Fluids are packaged, not poured

A space **pipe** does not carry fluid. It carries `FluidPackageItem`s — fluid packaged into
belt items — on ordinary item lanes. So pipes are already covered by anything that handles
item bundles, and their throughput in litres is packages × package size.

The port that *fills* a pipe is another matter. `SpaceFluidPortSenderSimulation` is an
`IFluidSimulation` that packages fluid straight into a buffer, with no lane to hook. Every
package it sends goes through one call, which is a clean detour target:

```csharp
DetourHelper.CreatePostfixHook<FluidPackageLaunchSimulation, Ticks, FluidPackageData, Ticks>(
    (launch, duration, package, excess) => launch.CreateNewLaunch(duration, package, excess),
    (launch, duration, package, excess) => Count(launch));
```

Map the launch simulation instance back to your own record — a port exposes its
`LaunchSimulation` field — and the ceiling is one package per `LaunchDuration_T`.

Fluid moving directly between docked platforms has no equivalent event: it flows through
connected containers as a network, so there is nothing discrete to count.

## There is more than one way off a platform

Totalling what a platform ships means finding its output ports, and they are separate
types:

| Simulation | Route out |
|---|---|
| `SpaceBeltPortSenderSimulation` | into a space belt |
| `SpaceFluidPortSenderSimulation` | packaged into a space pipe |
| `BeltPortTransferSimulation` | straight into a platform docked alongside, no belt involved |

Miss the third and a platform docked to its neighbour appears to ship nothing. Platform
size is irrelevant — it is the connection style that changes the type.

## Do not register the whole map in one frame

Walking every simulation on a large save and hooking its lanes takes long enough that
Windows marks the window unresponsive, which is indistinguishable from a hang. Snapshot the
list, then drain it over the following frames against a time budget:

```csharp
Pending = new List<ILocalizedSimulation>(Simulator.Simulations);   // cheap: a SelectMany
```

```csharp
Stopwatch slice = Stopwatch.StartNew();
while (PendingIndex < Pending.Count)
{
    // ...register a batch of 64...
    if (slice.ElapsedMilliseconds >= BudgetMilliseconds) break;
}
```

Anything built while the sweep is draining arrives through `ISimulator.OnSimulationCreated`
anyway, so it is safe to be slow. Log the totals when it finishes — the count tells you
what the mod is really carrying:

```
Tracking 48213 flows across 91002 simulations (1840ms)
```

## See also

- [Simulations and Item Lanes](../simulations-and-lanes.md) — the lane model and hooks
- [Read machine state](read-machine-state.md) — running, starved or blocked, without counting
- [Show a platform side panel](island-side-panel.md) — putting a measured rate in the HUD
- [Work with fluids](work-with-fluids.md) — containers and networks
