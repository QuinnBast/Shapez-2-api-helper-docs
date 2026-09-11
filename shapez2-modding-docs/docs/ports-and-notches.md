# Ports and Notches

How a platform connects to the rest of the factory, and how to work out what crosses a
boundary. This page is about the **real** simulation graph; the prediction graph has its own
parallel set of types, covered in
[Read what shapes will flow where](howto/read-shape-predictions.md).

## A platform's ports are buildings

The first thing to get straight, because it shapes everything else: an ordinary buildable
platform declares **no island connectors at all**. The `BiggerPlatforms` sample builds
platforms of every size and passes an empty array:

```csharp
return new IslandConnectorData(
    Array.Empty<EntityIO<LocalChunkPivot, IIslandConnector>>(),
    chunkLayout.ChunkPositions);
```

A normal platform docks to its neighbours by **notches**, and its ports are belt-port and
fluid-port **buildings** that the player places on it. `IIslandConnector` is for special
islands that swallow a space belt or pipe directly — the `SandboxIslands` sample's
FluidTrash is one.

Two consequences worth internalising:

- **Whether a port is an input or an output is not a property of the platform.** It is which
  port building sits at that tile — sender or receiver.
- If you are adding a platform that needs ports, you do not need a family of definitions for
  every port arrangement. One definition plus the game's own port buildings covers it.

## Notches

A notch is one side of one chunk. `NotchDefinition` fixes its geometry:

```csharp
private static int NotchPosX = 17;
private static readonly ChunkTileCoordinate[] EastNotchTilesL = new ChunkTileCoordinate[4] { ... };
public static readonly int NotchTileCount = EastNotchTilesL.Length;   // 4
```

Four tiles across, and `GetNotchLocationOnChunk_L(direction, index, short layer)` repeats
them on each building layer — so **one notch carries up to twelve ports** (4 positions × 3
layers).

A platform of `w × h` chunks has **`2(w + h)`** outward-facing chunk sides, so notch capacity
grows twice as fast as area. A 1×5 strip offers twelve notches for five chunks; a 3×3 offers
the same twelve for nine.

To place a port's tile within a notch, the game gives you two helpers:

```csharp
// Local: which of the four positions, given a direction. Ignores height.
NotchDefinition.TryGetIndexOfNotchLocation_L(tile_L, direction, out int index);

// Global: resolves the chunk and direction itself from an island definition.
NotchDefinition.TryGetIndexOfNotchLocation(pivot, islandTransform, islandDefinition, out int index);
```

The local form is the useful one if you also want to know *which side*: convert the tile to
chunk-local coordinates and try all four directions — the one that matches is the side the
port faces.

```csharp
GlobalChunkCoordinate chunk = tile.ToChunkCoordinate();
GlobalChunkTransform unrotated = new GlobalChunkTransform(chunk, GridRotation.NoRotate);
ChunkTileCoordinate local = tile.ToChunkCoordinate(in unrotated);
```

> [!TIP]
> If you are grouping a boundary for any purpose — describing it, reproducing it, comparing
> two of them — the **notch** is a far better unit than the individual port. A notch is what
> one connection to a neighbour occupies, so grouping by notch matches how the player thinks
> about their factory, and it avoids inventing which of four slots a belt "should" land in.

`GridRotation` has no `None`; the identity is **`GridRotation.NoRotate`**.

## What kind of simulation a port is

There is **no shared interface** over the port simulations, so identifying one means naming
every shape the game can leave a port in. All of these are boundary ports:

| Simulation | Situation | Direction |
| --- | --- | --- |
| `SpaceBeltPortReceiverSimulation` | space belt in | input |
| `SpaceBeltPortSenderSimulation` | space belt out | output |
| `SpaceFluidPortReceiverSimulation` / `SpaceFluidPortSenderSimulation` | space pipe | in / out |
| `BeltPortReceiverDisabledSimulation` | receiver with no sender opposite | input, unconnected |
| `BeltPortSenderBlockedSimulation` | sender with no receiver opposite | output, unconnected |
| `FluidPortReceiverDisabledSimulation` / `FluidPortBlockedSimulation` | same, for fluid | in / out, unconnected |
| `BeltPortTransferSimulation` | two docked platforms, belt | spans both |
| `FluidPortTransferSimulation` | two docked platforms, fluid | spans both |

The blocked and disabled variants are the ones people miss. `BeltPortSystem` creates them
through `CreateBlockedSenderSimulation` and `CreateOnlyReceiverSimulation` when a port
building has no counterpart — the game still simulates what such a port *would* carry, and
they are boundary ports by definition.

Their lanes are public and unterminated, which makes them the natural handles for
[a detached simulation](howto/run-a-detached-simulation.md):

```csharp
public readonly BeltLane InputLane;    // BeltPortSenderBlockedSimulation - nothing consumes it
public readonly BeltLane OutputLane;   // BeltPortReceiverDisabledSimulation - nothing fills it
```

## Do not use the connection graph to decide "external"

This is the trap, and it is a convincing one.

The obvious way to ask whether a port leaves a region is: *does anything it connects to lie
outside?* `ISimulator.FindAllConnectedSimulations` will happily answer.

For a space port the answer is **always no**. Its graph neighbours are all on its own
platform, because the hop across space is carried by a separate path simulation two tiles
out — so the most external thing on the platform reports as internal, and every space
connection silently disappears from your results.

Classify **structurally** instead, from the table above. A space port reaches across space
by construction; a blocked port has nothing opposite; only a transfer simulation needs a
question asked of it.

## Transfer simulations occupy two chunks

`ConnectablePort` wraps the transfer simulations, and it spans both platforms:

```csharp
public int NumOccupiedChunks => Input.Pivot...ToChunkCoordinate()
    == Output.Pivot...ToChunkCoordinate() ? 1 : 2;

public GlobalChunkCoordinate GetOccupiedChunk(int index)
    => index != 0 ? Output.Pivot.Position.ToChunkCoordinate()
                  : Input.Pivot.Position.ToChunkCoordinate();
```

So index `0` is the **sending** side and index `1` the **receiving** side. Two things follow:

- **Checking only `GetOccupiedChunk(0)` makes every inbound docked port invisible**, because
  an inbound port is anchored on the platform *outside* your region.
- Which end you own tells you the direction: own the sender, and items leave you; own the
  receiver, and they arrive. Own both, and it is an internal connection you can hide.

## Establish ownership before classifying

`map.Simulator.Simulations` holds **every port on the map**. A classifier that names port
types without first checking that the port belongs to the islands you care about will
cheerfully report the whole map's boundary — in one real case, 384 ports across 36 notches
for a platform one chunk in size.

Check every occupied chunk, not just the first, for the reason above:

```csharp
private static bool Touches(ILocalizedSimulation localized, IMapModel map, HashSet<IslandId> selected)
{
    for (int i = 0; i < localized.NumOccupiedChunks; i++)
    {
        if (map.TryGetIsland(localized.GetOccupiedChunk(i), out IslandModel owner)
            && selected.Contains(owner.Id))
        {
            return true;
        }
    }

    return false;
}
```

## Tile height is absolute

A port's tile `z` is measured from the bottom of the map, not the bottom of its platform. A
platform on island layer 1 reports tiles at `z` 20, 21, 22 for what the player sees as
building layers 0, 1 and 2.

`CoordinateConstants.TilesPerIslandLayer` is `20`, and the game itself reduces with
`FastMath.SafeMod(pivot.Position.z, 20)` inside `NotchDefinition`. Do the same before
comparing a layer to anything.

See [Coordinate Systems](coordinates.md) for the wider set of conversions.

> [!NOTE]
> Derived from the game's own port systems and from a mod that classifies boundaries, not
> from an official sample. The type names are stable enough to grep for, but verify against
> your game version.
