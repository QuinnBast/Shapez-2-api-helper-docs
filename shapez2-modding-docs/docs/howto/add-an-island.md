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
   .WithPerChunkColliders()          // never WithBoundingCollider - see below
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

For something that is *not* a platform — a piece of track, say — you want no frame at all.
Pass a zeroed context rather than removing the data:

```csharp
.WithRenderingOptions(new HomogeneousChunkDrawing(default), drawPlayingField: false)
```

`ChunkPlatformDrawingContext` is a struct of five bools, so `default` is "draw nothing".
Detaching `IslandFrameDrawData` instead looks equivalent and is not — see the gotcha below.

## Flipping with F is group membership, not a flag

`WithInteraction(flippable: true, …)` on its own does nothing. `IslandPlacersCreator`
decides by **counting** the group's `IslandGroupCollection`:

| Definitions in the group | Placer |
| --- | --- |
| 1 | `SinglePlacer` / `AreaPlacer` — F does nothing |
| 2 | `FlippableSinglePlacer` / `FlippableAreaPlacer` — F swaps between them |
| 3+ | throws |

So F needs a second, mirrored definition **in the same group**. The extender registers one
island per chain and a second chain would need its own group id — so build both from a
single `IIslandBuilder` that quietly registers the pair and returns the original. Attach
`FlippableDefinition` both ways as well: the placer works off group membership, but
`IslandBlueprintProcessor` reads that CustomData, and without it a flipped island comes
back unflipped out of a blueprint.

The engine mirrors over `ChunkAxis.YAxis`, and `ChunkDirection.Mirror` swaps a direction
only when that direction's own axis matches — North and South are the `YAxis` pair, East
and West the `XAxis` one. So a mirrored variant reverses its North/South connectors and
leaves East/West alone.

The catch: **the mirror never travels the extender chain**. Its simulation, prediction and
side-panel provider are all keyed by definition id, so each needs registering separately,
re-arming per scenario load the way `AtomicIslandExtender.Build` re-arms its own chain.

## Use `WithPerChunkColliders()`, not `WithBoundingCollider()`

`WithBoundingCollider()` sizes one box as `(max - min) * 20` over the chunk positions.
That is **one chunk short on every axis**, and since every island is a single layer deep,
`min.z == max.z` — so the box is always **zero height**, whatever the island's footprint.
A single-chunk island gets a box of zero size in all three axes.

The symptom is that the cursor passes straight through: the island cannot be hovered,
selected, pipetted or deleted, while everything vanilla around it works normally.

```csharp
.WithPerChunkColliders()   // 20×20×20 per chunk, centred at chunk * 20
```

That matches what vanilla's `CommonIslandDefinitionFactory.GenerateCollisionBoxes`
produces — `(count) * 20` per axis, greedy-meshed. The official `BiggerPlatforms` and
`SandboxIslands` samples both call `WithBoundingCollider()`, so do not take them as
evidence it works.

## Gotchas

- **Anything you attach in `BuildAndRegister` must be idempotent.** The builder runs its
  fluent chain once, when your mod is constructed, but `BuildAndRegister` is called again
  for *every scenario load* against those same `IslandDefinition` objects. `Detach<T>()`
  resolves with `Get<T>()` first and throws `NoDataFitDataTypeQueryException` when there
  is nothing there, and `RemoveFlag<T>()` is only `Detach<T>()` renamed. `Attach` is
  quieter and worse: it is a plain add, so a second copy sits *beside* the first and
  `CustomDataHolder` reports a multiple match as found-nothing — the thing works on a new
  save and silently stops on a loaded one. Use `AttachOrReplace`, or guard with `Has<T>`.
- **Never detach `IslandFrameDrawData`.** Vanilla attaches it to *every* island
  unconditionally, space belts included, just with an all-false context.
  `IslandChunkPlatformFramesCache.RegisterIsland` early-returns for an island that lacks
  it, so that island's chunks never enter the cache — while `IslandFramesDrawer.Draw`
  calls `GetEntry` for every culled chunk **with no guard at all**. The result is a
  `KeyNotFoundException` once per frame, forever, which also aborts `MapDrawer.Draw`
  partway through and silently kills whatever draws after it.
- **Layout is the time sink**, not the chain. Budget accordingly, and start from the
  closest sample rather than from zero.
- **The pipette map is a `Dictionary.Add`, and `.WithDefaultPlacement()` already claimed
  your island.** If you also register a second placer covering the same definitions — a
  path placer over a family of forwards and turns, say — the game's
  `CreateSpacePathPlacementInitiator` registers every member for pipetting too, and the
  second `Add` throws `An item with the same key has already been added` during
  `PlayerInteractionOrchestrator`'s constructor, before the main menu appears. The
  builder chain offers no way to skip `WithDefaultPlacement()`, so hand your own placer a
  throwaway `Dictionary<IEntityDefinition, PipettePlacementRequest>` instead of the real
  `IslandInitiatorsParams.PipetteMap`. Pipetting then resolves to the default placer,
  which is a fair trade for starting up. Which of the two rewirers runs first is not
  something to rely on — fix it so either order works.
- Island toolbar categories are different from building ones — `SandboxIslands` anchors
  at `ChildAt(5).ChildAt(4)`, nowhere near the building indices. See
  [Add to the toolbar](add-to-toolbar.md).
- `IslandDefinitionId` is written into saves. Renaming breaks existing saves.
- A platform that unlocks in the standard scenario but not the converter scenario is
  the classic milestone-id bug — see
  [per-scenario selection](add-research-unlock.md#unlock-at-a-milestone).
- Most `IslandGroup.Create(...)` options are stored and never read. ShapezShifter's
  `IslandGroupBuilder.BuildAndRegister` attaches only `GroupPresentationData`, so
  `AsNonTransportableIsland`, `WithPreferredPlacement`, `Removable`, `AutoConnected` and
  `AllowedOnNotches` have no effect. A declared `DefaultPreferredPlacementMode` never
  reaches the definition, and `CreateDefaultPlacer` falls through to single placement.
  Attach it to the definition yourself if you need it.
- For how the island is actually drawn — and why a space-path-like island needs an
  `IIslandPlatformDrawer` rather than mesh CustomData — see
  [Rendering](../rendering.md#how-an-island-gets-drawn).
