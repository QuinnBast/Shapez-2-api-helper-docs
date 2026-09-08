# Coordinate Systems

> **You need this when** something wants a `GlobalTileCoordinate` and you have a
> `float3`, when you are converting a building's position into somewhere to draw, or
> when you cannot work out why `Tile_G` and `Tile_I` are different types.
> In practice most of it reduces to one call — `ToCenter_W()` — and the rest is knowing
> which unit you are holding.


shapez 2 has a lot of coordinate types, and they are all distinct structs on purpose:
the compiler stops you handing a chunk coordinate to something expecting a tile. Once
you learn the suffix convention the API mostly reads itself.

## The suffix convention

Fields, parameters, and properties carry a suffix naming the space they are in.

| Suffix | Space | Example |
| --- | --- | --- |
| `_W` | **World** — Unity units, what the renderer wants | `CameraPosition_W`, `ToCenter_W()` |
| `_G` | **Global** — absolute in map space | `Tile_G`, `Bounds_G`, `Rotation_G` |
| `_I` | **Island** — relative to an island's origin | `Tile_I`, `Transform_I` |
| `_L` | **Local** — relative to the entity being drawn | `pos_L` |
| `_GC` | a global **chunk** coordinate | `chunk_GC` |
| `_SC` | a **super chunk** coordinate | `Origin_SC` |
| `_S` | **Steps** — the integer unit lanes advance in | `Progress_S`, `MaxStep_S` |
| `_T` | **Ticks** — simulation time | `Duration_T`, `remainingTicks_T` |

`_S` and `_T` are not spatial, but they follow the same discipline: `Steps` and `Ticks`
are distinct types, and mixing them is a compile error rather than a silent bug.

## The spatial units

```text
World  ──  Unity units (float3 / Vector3)
  ▲
  │  ToCenter_W()
  │
Tile   ──  one building cell            GlobalTileCoordinate, IslandTileCoordinate
  ▲
  │  a chunk is a fixed grid of tiles
  │
Chunk  ──  the island building grid     GlobalChunkCoordinate
  ▲
  │  a super chunk is a fixed grid of chunks
  │
SuperChunk ── culling / caching unit    SuperChunkCoordinate
```

**Tiles** are what buildings sit on. **Chunks** are what islands are made of — an
island's size and shape are expressed in chunks, and `IslandModel.Chunks` walks them.
**Super chunks** exist for the renderer: culling, mesh caching, and map-resource lookup
work per super chunk, which is why `HUDOverviewModeMapResourcesRenderer` caches one
combined mesh per `SuperChunkCoordinate`.

## Coordinates, vectors, transforms, bounds

For each level there are four related types:

| Kind | Purpose | Examples |
| --- | --- | --- |
| Coordinate | a position | `GlobalTileCoordinate`, `GlobalChunkCoordinate`, `WorldCoordinate` |
| Vector | a difference between positions | `TileVector`, `ChunkVector`, `WorldVector`, `LocalVector` |
| Transform | position + rotation | `GlobalTileTransform`, `GlobalChunkTransform`, `IslandTileTransform`, `LocalChunkTransform` |
| Bounds | a rectangular region | `GlobalTileBounds`, `GlobalChunkBounds`, `SuperChunkBounds`, `LocalTileBounds` |

Coordinate minus coordinate gives a vector; coordinate plus vector gives a coordinate.
`GridRotation` is the discrete rotation used everywhere in the grid, and `TileDirection`
names the four in-grid directions.

## Converting

### To world space

Everything that can be drawn converts through `ToCenter_W`, which returns the **centre**
of the cell rather than a corner. The optional argument is a vertical offset, which is
how the game layers flat overlays on top of each other:

```csharp
WorldCoordinate p1 = tile_G.ToCenter_W();          // centre of the tile
WorldCoordinate p2 = tile_G.ToCenter_W(2.0f);      // two units above it
WorldCoordinate p3 = chunk_GC.ToCenter_W(-23.0f);  // well below the platform
```

The interface `IConvertibleToWorldPosition` declares
`ToCenter_W(float heightOffset = 0f)`, and the coordinate and bounds types implement it.
`WorldCoordinate` casts to `Vector3` and interoperates with `float3`:

```csharp
float3 position = tile_G.ToCenter_W(1.0f);
Matrix4x4 matrix = Matrix4x4.TRS(position, Quaternion.identity, Vector3.one);
```

The game reserves specific heights for its own overlay planes — `HUDOverviewMode`
declares `LAYER_HEIGHT_PLANE = -25f`, `LAYER_HEIGHT_EXPLORED_AREA = -24f`,
`LAYER_HEIGHT_SHAPES = -23f`, `LAYER_HEIGHT_FLUIDS = -22f`. Pick your own offsets away
from those if you are drawing in overview mode.

### Between island space and global space

An island-relative tile needs the island's origin chunk to become global, and vice
versa:

```csharp
// island → global
GlobalTileCoordinate tile_G = tile_I.ToGlobal(in island.Transform.Position);
WorldCoordinate     pos_W  = tile_I.ToCenter_W(in island.Transform.Position);

// global → island
IslandTileCoordinate tile_I2 = tile_G.ToIslandCoordinate(in island.Transform.Position);
```

`BuildingModel` does this for you: `Tile_G` and `Tile_I` are both available, and
`Tile_I` is implemented as exactly the conversion above.

### Applying a transform

Local geometry becomes global by being pushed through the owning entity's transform:

```csharp
GlobalTileCoordinate tile = tileVector.ToGlobal(in building.Transform);
GlobalTileBounds bounds   = localBounds.ToGlobal(in building.Transform);
```

This is how `BuildingModel.Tiles_G` turns a definition's local tile list into the
building's actual footprint, rotation included — which is why you should use it rather
than adding offsets to `Tile_G` yourself.

### Bounds

```csharp
GlobalChunkBounds bounds = GlobalChunkBounds.From(island.Chunks);
WorldCoordinate   centre = bounds.ToCenter_W(0.0f);
Bounds        worldBounds = bounds.ToWorldBounds();  // Unity Bounds, for culling
GlobalTileBounds tiles    = bounds.ToTileBounds();   // same region, in tiles
```

`IslandModel.Bounds` is `GlobalChunkBounds.From(Chunks)` — it enumerates every chunk on
each call, so cache it if you need it repeatedly.

## Practical guidance

- **Never store `float3` when you could store a coordinate.** The typed coordinate is
  the one that survives a refactor; world positions are a rendering detail.
- **Convert as late as possible** — do your logic in tile or chunk space and go to world
  space only at the draw call.
- **`in` parameters are everywhere** in this API because these are structs; match the
  signature (`ToGlobal(in transform)`) rather than fighting it.
- When you cannot find a conversion, look for an extension method: several live in
  `Game.Core.Coordinates.Extensions` and on the bounds types rather than on the
  coordinate itself.
