# Draw in the world

**Problem.** You want a coloured marker over a building, or a tint over a whole
platform.

**Solution.** Postfix the map draw call and submit a mesh. Nothing in shapez 2 is a
`GameObject` per building, so there is nothing to parent to — you draw every frame from
the model.

## Get into the frame

```csharp
using ShapezShifter.SharpDetour;
using MonoMod.RuntimeDetour;

private readonly Hook DrawHook;

public MyMod(ILogger logger)
{
    DrawHook = DetourHelper.CreatePostfixHook<MapDrawer, FrameDrawOptionsNoLOD>(
        (drawer, options) => drawer.Draw(options),
        (drawer, options) => Draw(options));
}

public void Dispose() => DrawHook?.Dispose();
```

Postfix means you draw *after* the game's own drawers, which is what you want for an
overlay. The `options` parameter carries everything you need, including
`options.Player.CurrentMap` — no global lookup.

The first lambda is never executed; it is parsed as an expression tree to identify the
target method.

## Draw a coloured plane on a tile

```csharp
private void Draw(FrameDrawOptionsNoLOD options)
{
    if (!Enabled) return;

    IMapModel map = options.Player?.CurrentMap;
    if (map == null) return;

    IMaterialReference material = options.Theme.BaseResources.UXOverviewModeMapResourcePlaneMaterial;

    foreach (BuildingModel building in InterestingBuildings)
    {
        float3 position = building.Tile_G.ToCenter_W(2.0f);   // 2 units above the tile

        options.Renderers.RegularNonInstanced.DrawMesh(
            GetPlaneMesh(ColorFor(building)),
            material,
            Matrix4x4.TRS(position, Quaternion.identity, new Vector3(20f, 20f, 20f)),
            RenderCategory.Misc,
            MaterialPropertyHelpers.CreateAlphaBlock(0.7f));
    }
}
```

## Colour: bake it into the mesh

The material shader used above takes alpha, not an arbitrary colour, so the game bakes
colour into the *mesh* and caches one mesh per colour. Copy that:

```csharp
private readonly DisposableDictionary<Color, TemporaryMeshReference> PlaneMeshes = new();

private IMeshReference GetPlaneMesh(Color color)
{
    if (!PlaneMeshes.TryGetValue(color, out TemporaryMeshReference mesh))
    {
        mesh = GeometryHelpers.GeneratePlaneMeshUVColoredUncached(color);
        PlaneMeshes.Add(color, mesh);
    }
    return mesh;
}
```

Quantise your colours so the cache stays small — an eleven-step red→green ramp is
plenty for an overlay, and gives you eleven meshes instead of thousands.

Dispose the dictionary in `Dispose()`. Leaked `TemporaryMeshReference`s are a known
enough trap that the class ships a `WarnAboutNonDisposedInstances()` helper.

## Only in overview mode

```csharp
if (!options.InOverviewMode) return;   // Viewport.Zoom > 1500f
```

This is how the game swaps detailed island meshes for flat overview planes, and it is
the natural switch between "tint each machine" (zoomed in) and "tint each platform"
(zoomed out).

Overview mode reserves specific heights for its own planes — `-25` background, `-24`
explored area, `-23` shapes, `-22` fluids. Pick offsets away from those.

## Cover a whole platform

```csharp
foreach (GlobalChunkCoordinate chunk in island.Chunks)
{
    float3 position = chunk.ToCenter_W(-20.0f);
    // …DrawMesh with a chunk-sized scale
}
```

## Batch it before you ship it

One `DrawMesh` per building is fine for a few hundred and painful for ten thousand.
`MeshBuilder` combines them into a single mesh:

```csharp
using MeshBuilder builder = new MeshBuilder("MyOverlay(island)", lod: 0);

foreach (GlobalChunkCoordinate chunk in island.Chunks)
{
    builder.AddTranslateScale(planeMesh, chunk.ToCenter_W(-20f), new float3(20f, 20f, 20f));
}

if (builder.Empty) return null;
TemporaryMeshReference combined = builder.GenerateSingleMeshMax65KVertices();
```

Build these **once and cache**, keyed by whatever invalidates them — the island's
contents, and your own colour buckets. Rebuilding a combined mesh every frame is worse
than not combining at all. The game caches per `SuperChunkCoordinate` and invalidates on
`map.OnBuildingAdded` / `OnIslandAdded` and friends.

## Gotchas

- `MaterialPropertyHelpers.CreateAlphaBlock` and `CreateBaseColorBlock` mutate and
  return a **shared static** block. Submit the draw immediately; never hold one, and
  never expect to combine both calls. Keep your own `MaterialPropertyBlock` if you need
  two properties at once.
- `options.Theme` is `[Obsolete]` ("should be injected to drawers") but is the only easy
  route to the shared materials.
- Cull. `MapDrawer.LastCullResult` tells you which super chunks are visible; drawing the
  whole map when the camera sees one corner is the most common performance mistake.
- Exceptions thrown from a postfix are *not* caught the way sub-drawer exceptions are.
  Wrap your draw body in `try`/`catch` or one bad frame kills every frame.
