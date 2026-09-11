# Train cargo tools - design notes

What was read out of the decompiled assemblies rather than assumed, so none of it has to be
re-derived.

## The idea

Train cargo currently only exists inside a train station. The mod takes it outside:

1. **Cargo belts** - a belt that carries train cargo packages, so cargo can be buffered on belts
   instead of being stuck in a station's small container buffer.
2. **A packager / unpackager pair** - space buildings that turn loose shapes into cargo packages
   and back.

The motivation is buffering: a station holds only so many containers, so a stalled train stalls
the whole line. Cargo on belts is buffer capacity you can build more of, and one belt slot holds
a whole package rather than one shape.

## Status: shape and fluid packagers, package-eating stations, 2026-09-10

Builds clean. Five islands, each one chunk, input West and output East:

| Island | West in | East out |
|---|---|---|
| `CargoBelt` | belt (cargo) | belt (cargo) |
| `CargoPackager` | belt (shapes) | belt (cargo) |
| `CargoUnpackager` | belt (cargo) | belt (shapes) |
| `FluidCargoPackager` | **pipe** (fluid) | belt (cargo) |
| `FluidCargoUnpackager` | belt (cargo) | **pipe** (fluid) |

`CargoBelt` is a straight space path whose lanes accept only `PackageOnTrack<CargoPackage<...>>`,
enforced by a `PreAcceptHook`. 32 slots per lane rather than the vanilla 16, since buffering is
the point.

**One cargo belt carries both kinds.** Its accept hook admits shape and fluid packages alike, so
there is no fluid cargo belt - only fluid *ends*. Cargo always travels the belt side even on the
fluid pieces, because the cargo belt reuses belt connectors.

The packagers and unpackagers are one generic implementation each, instantiated at `ShapeId` and
`FluidId`, which is how the game itself keeps the two apart. The only per-type differences are
the converter and the capacity provider (`ShapePackageSize` vs `FluidPackageSize`).

Plus five detours (`PackagedCargoStations`) so **every train station, shape and fluid, accepts
packages directly** - see below. The end-to-end shape is therefore
`items -> packager -> cargo belt -> train station`, with an unpackager needed only where cargo
has to go back to ordinary machinery.

**The cargo belt is now testable.** The previous build shipped the belt alone and nothing in the
game could put a package on it; a packager can.

### How the two new pieces are built

Almost none of the packing logic is new. `TrainBeltToCargoFillingContainer<ShapeId>` and
`TrainCargoToBeltFillingContainer<ShapeId>` are the game's own classes - the ones every shape
station already uses - re-hosted in an island simulation with the train removed:

| | Station | This mod |
|---|---|---|
| Pack | `TrainBeltToCargoFillingContainer` fills a package, loader hands it to a `CargoPackageTrack` for a train | same filling container, packager wraps it in `PackageOnTrack` and hands it to the output belt |
| Unpack | wagon loads a package into `TrainCargoToBeltFillingContainer`, which drains it onto belts | `CargoPackageReceiver` takes the package off a belt into the same state, same filling container drains it |

`CargoPackageReceiver` is the one genuinely new class, and only because vanilla never needs it:
unloading always starts from a train, so `TrainCargoToBeltFillingContainer` has a `LoadPackage`
method and no receiver side. It accepts a package only while its state is empty, which is what
gives an unpackager correct back-pressure.

Package size is not a mod constant. Both factories read
`GameMode.TrainCargoExchangeConfiguration`, the same `ITrainCargoExchangeSimulationConfig` the
stations are built from, so a package off a packager is byte-identical to one a station would
have made and wagon-capacity research applies.

### What to check first

1. **That a package survives the round trip.** Packager -> cargo belt -> unpackager, one shape
   type, and count the output rate. It should match the input rate.
2. **That a station eats one.** Packager -> cargo belt -> train loader. Watch for the throw the
   third detour exists to prevent, and try deliberately merging a loose-shape belt and a packed
   belt into one station layer, which is the case that would have thrown.
3. **Rendering.** Still the open one. The existing cargo renderers are station renderers; a
   package travelling a space path may well draw as nothing, so a working cargo belt may look
   like an empty one. Check `BeltLaneRenderingDefinition` dispatch;
   `DiagonalCutter/MyBeltLaneRenderingDefinition.cs` in the samples fork is the worked example.
4. **Mixed shapes into one layer.** All four lanes of a layer pack into one package, exactly as
   at a station, so two shape types on one layer will fight through the filling container's
   subtractive penalty. Expected, but confirm it degrades rather than throws.
5. **Saving.** Should work (see below) but has never actually been exercised.

### Known rough edges

- **Placeholder icons.** Two generated PNGs, not art.
- Unlocked at the *first* milestone so they can be tested without an endgame save. Wrong for
  balance, deliberate for now.
- `AffectsSaveGames: true`, so the mod cannot be added to or removed from an existing save.
- **The fluid blob size is vanilla's 60 litres**, hardcoded, copied from
  `FluidCargoStationSimulationCreator`. An unpackager has to emit the same blob size a station
  does or the two would disagree about what one unit of fluid cargo is worth, so this is
  correct rather than lazy - but it is a literal, and if the game ever makes it configurable
  this is the place that will silently go stale.
- **Fluid cargo counts blobs, not litres.** `FluidBeltItemToCargoConverter` counts one
  `FluidPackageItem` as one unit of cargo regardless of its volume. That is vanilla's own
  accounting at a fluid station, inherited deliberately so a packager and a station produce
  interchangeable cargo - but it means packaging a non-60L blob loses or gains fluid, exactly as
  it does at a station today.

## Stations eat packages - resolved, global

`PackagedCargoStations` detours five methods so every train station accepts a
`PackageOnTrack<CargoPackage<ShapeId>>` as readily as a `ShapeItem`, and a
`PackageOnTrack<CargoPackage<FluidId>>` as readily as a `FluidPackageItem`. **A packed line runs
straight into a station; no unpackager in front.** The unpackager is still there for feeding
ordinary machinery, but it is no longer on the critical path.

Why three, and why split the way they are:

| Detour | Why |
|---|---|
| `ShapeBeltItemToCargoConverter.BeltItemTypeMatchesCargoItemType` | opens the gate - `item is ShapeItem` becomes "or a package" |
| `ShapeBeltItemToCargoConverter.TryConvertBeltItemToCargoItem` | unwraps rather than converts, reporting the package's real `Amount` so `TryGive` absorbs the whole thing in one hand-over |
| `DummyLane.CanAcceptItem` | closes the throw described below - one hook covers both item types |

with the first two repeated for `FluidBeltItemToCargoConverter`.

Keeping the first two on the *converter* rather than replacing `HandOverItem` wholesale is
deliberate: the station's own hand-over still runs, so it still sets
`LastTimeMatchingItemWasReceived`, which `IsLayerActive` reads to tell the train scheduler a
layer is alive. Reimplementing the hand-over would have silently broken train scheduling.

The converter pair is spelled out longhand for each item type rather than made generic. These
are the parts of the mod the compiler cannot check, so being able to read exactly what is
hooked is worth the duplication.

### MonoMod will not hook a method on a generic type. At all.

This cost a failed launch and is the single most useful thing learned here.

The guard belongs on `TrainBeltToCargoFillingContainer<T>.CanAcceptItem`, and that is where it
was first written - one hook at `<ShapeId>`, one at `<FluidId>`, on the reasoning that a struct
instantiation has its own native code and is therefore an ordinary method. That reasoning is
wrong. `Hook.CheckSupported` rejects the method outright:

```
System.ArgumentException: Source method is generic, generic hooks are not supported
  at MonoMod.RuntimeDetour.Hook.CheckSupported()
```

It compiles cleanly and throws at mod load, taking the whole game's startup with it. Struct vs
reference instantiation makes no difference; the check is on the declaring type.

**The fix is to find a non-generic choke point the calls already pass through.** Here that is
`DummyLane.CanAcceptItem`: a station's input bundle is a bundle of `DummyLane`s whose `NextLane`
is the layer's filling container, so an upstream belt asks the lane, not the container, first.
One hook there covers shape and fluid stations both - and covers this mod's own packagers,
which are wired the same way and could otherwise have thrown the same throw.

The cost is that a hot, widely used vanilla method now carries a type test. It is ordered so the
common case - a loose item, not a package - fails two `isinst` checks and falls straight through
to the original.

`PackagedCargoStations` also unwinds on failure now. The converter detours applied *without* the
guard is the one combination worse than doing nothing, since stations would accept packages and
then throw on a half-filled layer, so a partial application disposes what it applied.

**Consequence to watch in play:** a station fed a packed line ingests at `ShapePackageSize` times
the loose-shape rate. That is what packing is *for*, but it is a real balance change and it
applies to every station in the game, with no per-building opt-out.

### The throw that made the third detour necessary

Swapping only the converter is not enough. `HandOverItem` calls `TryGive`, which clamps `actual`
to `PackageSize - Package.Amount` and then throws
`Expected to give all when trying to give {amount}` if `actual != amount`. `CanAcceptItem` only
checks *not full*, never *has room for `amount`*, and a converter does not get to override
`CanAcceptItem`. Vanilla never trips this because a loose shape is always amount 1. A layer
holding four loose shapes that then received a ten-package would throw.

The guard is `Package.Amount == 0` - necessary and sufficient, because a package is full by
construction: both a packager and a station only ever emit at `PackageSize`.

## Background: why the station refused in the first place

**A vanilla train loader will not accept a package.** This is the central constraint and it is
worth stating precisely:

- `TrainCargoLoaderSimulation` wires every input lane's `NextLane` to a
  `TrainBeltToCargoFillingContainer<TItem>`.
- `TrainBeltToCargoFillingContainer.CanAcceptItem` is
  `!Package.IsFull(cap) && CargoConverter.BeltItemTypeMatchesCargoItemType(item)`.
- `ShapeBeltItemToCargoConverter.BeltItemTypeMatchesCargoItemType` is `item is ShapeItem`.

A `PackageOnTrack<CargoPackage<ShapeId>>` is not a `ShapeItem`, so the station refuses it and the
cargo belt backs up at the door. There is no other way in - the filling container is the only
entry point for cargo at a station.

That is what `PackagedCargoStations` above undoes.

`DetourHelper`'s prefix/postfix helpers were no use here - they cannot change a return value and
cannot express `out` parameters - so the hooks are raw `MonoMod.RuntimeDetour.Hook`s with
hand-written delegate types. `MonoMod.RuntimeDetour` is a new package reference on this project,
matching the version the other mods in this repo use.

### Still open: the unload direction

Applies to both item types equally. `TrainCargoUnloaderSimulation` takes an
`ICargoToBeltItemConverter<TItem>`, and
`TrainCargoToBeltFillingContainer` only peeks and pops against whatever the downstream lane will
accept - no capacity assertion, no throw. A converter whose `PopCargoIntoBeltItem` emits a
`PackageOnTrack` would give a station that unloads *straight onto a cargo belt*, with no other
change and none of the difficulty the load direction had. Not implemented; it is the obvious
next symmetry.

`TrainCargoUnloaderSimulation` takes an `ICargoToBeltItemConverter<TItem>`, and
`TrainCargoToBeltFillingContainer` only peeks and pops against whatever the downstream lane will
accept - no capacity assertion, no throw. A converter whose `PopCargoIntoBeltItem` emits a
`PackageOnTrack` gives a station that unloads straight onto a cargo belt, with no other change.

### Per-island or global? - decided global

`CargoExchangingController` is keyed on the concrete station simulation type and every shape
station is produced by the one `ShapeCargoStationSimulationFactory`, which has no per-island
branch. A separate "Cargo Train Loader" building would therefore have needed the new island
registered into `TrainIslandCollection.Exchange.ShapeLoaders` *and* a detour on
`ShapeCargoStationSimulationFactory.Produce` branching on `island.Definition.Id` - strictly more
work, and the registration half was never verified.

Global was chosen instead: stations simply understand packages, the way they arguably should
have. The cost is that the balance change is not opt-out.

## Verified facts

| Question | Answer | Where |
|---|---|---|
| Is a cargo container already a belt item? | **Yes.** `public class PackageOnTrack<TContainer> : IBeltItem, IItem, IPoolable where TContainer : struct` | `PackageOnTrack.cs` |
| Does cargo already travel on belt lanes? | **Yes.** `CargoPackageTrack<TPackage>` owns a real `BeltPathLane`, hands it `PackageOnTrack` items, and reads them back out of `BeltSlotState.Item` | `CargoPackageTrack` |
| **Does cargo on a belt lane save?** | **Yes - this is settled.** `BeltItemSerializer` already has tag 4 for `PackageOnTrack<CargoPackage<ShapeId>>` and tag 3 for the fluid one, and `BeltSlotState.Sync` goes through `visitor.Serialize(Item)` / `Deserialize<IBeltItem>()` generically. Nothing about it is station-specific, so a package on a modded lane round-trips | `BeltItemSerializer.cs`, `BeltSlotState.cs` |
| What travels - a container or a package? | A **package**. `CargoPackageTrack<CargoPackage<TItem>>` puts `PackageOnTrack<CargoPackage<TItem>>` on the lane. `CargoContainer` (a bounded list of packages) only ever exists inside a wagon | `TrainCargoLoaderSimulation` |
| What is a cargo package? | `struct CargoPackage<TItem> { short Amount; TItem Item; }` - one item type plus a count, capped at `PackageSize` | `CargoPackage.cs` |
| Where do the cargo numbers come from? | `GameMode.TrainCargoExchangeConfiguration`, a public field, implementing `ITrainCargoExchangeSimulationConfig` (`ShapePackageSize`, `MaxPackagesPerContainer`, ...) | `GameMode.cs:36` |
| Do bundling and unbundling interfaces exist? | **Yes, both**, and both are constructor arguments of the station simulations | `IBeltToCargoItemConverter`, `ICargoToBeltItemConverter` |
| Who implements them today? | `ShapeBeltItemToCargoConverter` / `ShapeCargoToBeltItemConverter`, wired in by `ShapeCargoStationSimulationFactory` | `Game.Content/` |
| Can a vanilla loader eat a package? | **No.** `CanAcceptItem` -> `BeltItemTypeMatchesCargoItemType` -> `item is ShapeItem` | `TrainBeltToCargoFillingContain.cs` |
| Is a converter swap enough to fix that? | **No.** `HandOverItem` throws unless the filling container can absorb the whole amount, and `CanAcceptItem` does not check that | `TrainBeltToCargoFillingContain.cs` |
| How is an island's input wired to a non-lane receiver? | A bundle of `DummyLane`s; `DummyLane` forwards `CanAcceptItem` / `HandOverItem` straight to `NextLane`. This is how the vanilla loader feeds its filling containers | `DummyLane.cs`, `TrainCargoLoaderSimulation` |
| How many lanes in a bundle? | 12 - `NumLanes = 4` x `NumLayers = 3` | `SpacePathConstants` |
| Can one simulation have separate in and out bundles? | **Yes.** `ConnectableIslandSimulation` pairs receiver bundle *i* with the *i*-th `ISpacePathInputConnector` and provider bundle *j* with the *j*-th output, independently | `ConnectableIslandSimulation` |
| Are `SyncableIdentifier`s inheritable? | **No** - `GetCustomAttributes(inherit: false)`, and the table is keyed by exact runtime type. Every saved state class needs its own concrete type and its own id | `PolymorphicSerializer.cs:34` |
| Are there existing cargo item renderers? | Yes for space paths: `ShapeSpacePathBeltItemRenderer`, `FluidSpacePathBeltItemRenderer` | `SPZGameAssembly/` |

## Two constraints found while building the belt

- **Only two connector families exist.** `ConnectableIslandSimulation` switches on the connector
  class to pick the chunk connector's item type (`SpaceBeltInputConnector` to `ShapeItem`,
  `SpacePipeInputConnector` to `FluidPackageItem`) and throws `NotImplementedException` for
  anything else. A dedicated cargo connector would need that class replaced, so all three pieces
  reuse the ordinary belt connectors.
- **But the item type there is only a compatibility tag.**
  `ItemInputChunkConnector<TItem>.CanConnect` tests `other is IItemOutputChunkConnector<TItem>`,
  which decides which paths may *join*; what flows is whatever the lanes accept. That is why the
  accept hook works, and why a packager can emit packages through a shape-tagged connector. The
  visible cost: a cargo belt will connect to an ordinary space belt and then silently refuse its
  shapes. Needs solving either by replacing `ConnectableIslandSimulation` or by making the
  refusal legible in the UI.

## Open questions

- **Rendering outside a station** - the biggest remaining unknown, and now the only thing between
  a working chain and a chain that *looks* working.
- **Do buildings accept containers?** Deliberately dodged: an unpackager sits in front of
  ordinary machinery. Train stations were the case worth solving properly, and they are solved.
- **Balance.** A cargo belt moves `PackageSize` times more per slot than a shape belt. That is
  the point, but it needs a cost and a research gate.
