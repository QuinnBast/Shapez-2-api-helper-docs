# Work with fluids

**Problem.** You want to read how full a tank is, what a pipe network is carrying, or
give your own building fluid ports.

Fluids work differently from shapes: shapes are discrete items on lanes, fluids are
**quantities in containers joined into networks**.

## Containers

```csharp
public interface IFluidContainer
{
    IFluid Fluid { get; }          // null when empty
    FluidUnit Capacity { get; }
    FluidUnit Value { get; }       // current amount
    float Level { get; }           // 0..1 — the one you want for a UI bar

    bool CanAdd(FluidUnit amount, IFluid fluid);
    void Add(FluidUnit amount, IFluid fluid);
    void Take(FluidUnit amount);
}
```

`Level` is already normalised, so a fill bar is one property read. `Fluid` is `null`
when the container is empty — always null-check before comparing fluid types.

`FluidUnit` is a distinct type, not a float, in the same spirit as `Ticks` and `Steps`
([coordinates](../coordinates.md#the-suffix-convention)). Do not try to mix it with raw
numbers.

## Networks

Connected pipes form an `IFluidNetwork`:

```csharp
public interface IFluidNetwork
{
    IFluid Fluid { get; }
    int ComponentCount { get; }
    IEnumerable<IFluidProvider> Providers { get; }
    IEnumerable<IFluidReceiver> Receivers { get; }
    FluidNetworkSimulation.State CurrentState { get; }
    float ApproximatedNetworkSpeed { get; }
    int Uid { get; }
}
```

This is the fluid analogue of "is this machine starved or backed up", and it is more
directly readable than the lane version:

- **`Providers` / `Receivers`** — what feeds the network and what drains it. A network
  with receivers and no providers is starved by construction.
- **`ApproximatedNetworkSpeed`** — throughput, already computed. Use this rather than
  sampling containers over time.
- **`CurrentState`** — the network simulation's own state enum.
- **`ComponentCount`** — how many pipes/entities are joined. A suspiciously large number
  usually means the player accidentally merged two networks.

Networks are rebuilt when pipes change, so do not cache an `IFluidNetwork` across
building edits — re-resolve it, and treat `Uid` as an identity for "same network as last
time I looked".

## Finding fluid state on a building

Same route as any machine — go through the simulator, then check the state or simulation
for a fluid interface:

```csharp
if (map.Simulator.TryFindTileSimulation(building.Tile_G, out ILocalizedTileSimulation localized))
{
    if (localized.Simulation is IFluidContainer container)
    {
        float level = container.Level;
    }
}
```

Some buildings expose fluid state through their simulation state instead
(`IFluidNetworkBuildingState`, `IFluidContainerConfiguration`). If the cast above
fails, check `building.State`:

```csharp
if (building.State.Is<SomeFluidState>(out var fluidState)) { … }
```

See [read machine state](read-machine-state.md) for the general pattern and
[the map model](../map-model.md#reading-state) for the state container.

## The fluid registry

```csharp
IFluidRegistry registry = GameHelper.Core.FluidRegistry;

IFluid fluid = registry.GetFluidReference(fluidId);
FluidId id   = registry.GetFluidId(fluid);
FluidId newId = registry.RegisterFluid(myFluid);   // registering your own
```

Note `FluidRegistry` is constructed from the game's shape colours
(`FluidRegistry(IReadOnlyList<IShapeColor> colors)`) — vanilla fluids are the paints,
so a "new fluid" is a colour-shaped concept, not an arbitrary liquid.

> [!NOTE]
> `RegisterFluid` exists and is public, but no sample registers a fluid. Treat adding a
> new fluid type as unexplored territory — verify against your game version before
> building on it.

## Giving a building fluid ports

Fluid IO is declared on the connector data, alongside shape and signal IO:

```csharp
BuildingFluidInput      // consumes
BuildingFluidOutput     // produces
BuildingFluidJunction   // passes through
```

Build them into the connector data the same way shape connectors are added:

```csharp
IBuildingConnectorData connectors = BuildingConnectors.SingleTile()
   .AddFluidInput(/* fluid connector config */)
   .AddFluidOutput(/* … */)
   .Build();
```

See [Add a building](add-a-building.md#the-building) for the surrounding chain.

There are also fluid *ports* between platforms
(`FluidPortBlockedSimulation`, `FluidPortSenderBlockedState`) — the platform-edge
transfer mechanism, separate from within-platform pipes. A blocked fluid port is the
fluid equivalent of a backed-up belt port, and the game renders it with
`FluidPortBlockedRenderer`.

## Space pipes carry items, not fluid

A space pipe is not a fluid network stretched across space. It carries `FluidPackageItem`s
— fluid packaged into belt items — on ordinary item lanes, as an `IItemBundleSimulation`.
So a pipe's throughput is packages per minute times the package size, and code written
against item lanes covers pipes for free.

The port that fills one is where it gets awkward. `SpaceFluidPortSenderSimulation` is an
`IFluidSimulation` that consumes from a tank and packages straight into a buffer — there is
no lane to hook and no per-transfer event on `IFluidReceiver`. What it does have is a
single call per package:

```csharp
DetourHelper.CreatePostfixHook<FluidPackageLaunchSimulation, Ticks, FluidPackageData, Ticks>(
    (launch, duration, package, excess) => launch.CreateNewLaunch(duration, package, excess),
    (launch, duration, package, excess) => Count(launch));
```

A port exposes its `LaunchSimulation`, so you can map instances back to whatever record you
keep. Its ceiling is one package per `LaunchDuration_T`, and its backlog is the tank's
`Level`.

Fluid crossing between two directly docked platforms has no such event — it flows as a
connected network, so there is nothing discrete to count. Sampling container levels cannot
recover it either, because a container that is passing fluid through at a steady rate holds
a steady level.

## Gotchas

- **`Fluid` is null on an empty container.** Comparing `container.Fluid == someFluid`
  without a null check is the standard bug here.
- `FluidUnit` and `float` are different types on purpose. Convert deliberately.
- Do not `Add`/`Take` on containers you do not own. The network simulation balances
  them, and mutating from outside desynchronises its state.
- Fluid networks are graph objects rebuilt on edits — resolve them fresh rather than
  holding references.
- `IFluid : IItem`, so fluids and shapes share the item hierarchy but not the transport
  model. Code written against lanes does not transfer to fluids — **except** for space
  pipes, which package fluid into items and are transported exactly like belts.
- `IFluidReceiver.Give` and `IFluidProvider.Take` have no hook equivalent. To observe
  fluid movement you either sample levels or find a discrete event, such as the package
  launch above. `IFluidProvider.Next` is settable, but inserting your own proxy into a
  live network risks breaking the player's factory - not worth it for an observer.
