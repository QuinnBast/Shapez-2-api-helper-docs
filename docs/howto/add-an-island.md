# Add an island or platform

**Problem.** You want a new platform shape, or an island with its own behaviour.

**Solution.** `AtomicIslands.Extend()` — the same state-machine chain as
[buildings](add-a-building.md), with island-shaped stages.

```csharp
AtomicIslands.Extend()
   .AllScenarios()
   .WithIsland(islandBuilder, islandGroupBuilder)
   .UnlockedAtMilestone(new ByIndexMilestoneSelector(^1))
   .WithDefaultPlacement()
   .InToolbar(ToolbarElementLocator.Root().ChildAt(5).ChildAt(4).ChildAt(^1).InsertAfter())
   .WithSimulation(new MyIslandFactoryBuilder())
   .WithoutModules()          // or WithCustomModules(...)
   .Build();
```

Two differences from the building chain worth noting: island `WithSimulation` takes no
logger, and the modules stage has an explicit `WithoutModules()` — a platform with no
HUD panel is normal, so you say so rather than skipping the stage.

## The group

```csharp
IslandDefinitionGroupId groupId = new("MyIslandGroup");

ModFolderLocator resources = ModDirectoryLocator.CreateLocator<MyMod>().SubLocator("Resources");

IIslandGroupBuilder group = IslandGroup.Create(groupId)
   .WithTitle("my-mod.island.title".T())
   .WithDescription("my-mod.island.description".T())
   .WithIcon(FileTextureLoader.LoadTextureAsSprite(resources.SubPath("Island.png"), out _))
   .AsNonTransportableIsland()
   .WithPreferredPlacement(DefaultPreferredPlacementMode.Area);
```

`DefaultPreferredPlacementMode.Area` is the drag-a-rectangle behaviour you want for
platforms; buildings usually want `LinePerpendicular`.

## The island

```csharp
ChunkLayoutLookup<ChunkVector, IslandChunkData> layout = FoundationLayout();

IIslandBuilder island = Island.Create(new IslandDefinitionId("MyIsland"))
   .WithLayout(layout)
   .WithBoundingCollider()
   .WithConnectorData(FoundationConnectors(layout))
   .WithInteraction(flippable: false, canHoldBuildings: false)
   .WithDefaultChunkCost()
   .WithRenderingOptions(ChunkDrawingOptions(), drawPlayingField: true);
```

`canHoldBuildings: false` makes it a fixed-function island (the trash island in
`SandboxIslands`); `true` makes it a build surface (the foundations in
`BiggerPlatforms`).

## The layout is the hard part

An island's shape is a `ChunkLayoutLookup` mapping `ChunkVector` → `IslandChunkData`,
and there is **no fluent builder for it yet** — the sample carries a
`// TODO: Create fluent API for this` comment. You construct chunk data directly:

```csharp
IslandChunkData chunkData = IslandLayoutFactory.CreateIslandChunkData(
    chunkTile: origin,
    notchDirections: Array.Empty<ChunkDirection>(),
    neighborChunks: origin.AsEnumerable(),
    isBuildable: true,
    flipped: false,
    out _);
```

The parameters that matter:

- **`neighborChunks`** — every chunk in the island, so each chunk knows what it borders.
  Get this wrong and edges render as if the platform ends mid-tile.
- **`notchDirections`** — where the connector notches sit, i.e. how the platform links
  to its neighbours.
- **`isBuildable`** — whether players can place buildings on this chunk.
- **`TileVoidFlags_L`** — per-tile void flags on the returned data, for punching holes
  in a chunk.

For anything beyond a single chunk, copy `BiggerPlatforms` — it exists specifically to
demonstrate multi-chunk foundation layouts (4×4, 5×5, 6×6, 5×1, 6×1) and is much
faster to adapt than deriving the data by hand.

## Rendering

```csharp
private IChunkDrawingContextProvider ChunkDrawingOptions()
{
    return new HomogeneousChunkDrawing(ChunkPlatformDrawingContext.DrawAll());
}
```

`HomogeneousChunkDrawing` draws every chunk the same way, which is what you want for a
uniform platform. `drawPlayingField: true` draws the build grid on top.

## Gotchas

- **Layout is the time sink**, not the chain. Budget accordingly, and start from the
  closest sample rather than from zero.
- Island toolbar categories are different from building ones — `SandboxIslands` anchors
  at `ChildAt(5).ChildAt(4)`, nowhere near the building indices. See
  [Add to the toolbar](add-to-toolbar.md).
- `IslandDefinitionId` is written into saves. Renaming breaks existing saves.
- A platform that unlocks in the standard scenario but not the converter scenario is
  the classic milestone-id bug — see
  [per-scenario selection](add-research-unlock.md#unlock-at-a-milestone).
