# Rendering

> **You need this when** you are putting something on screen and it has to stay fast.
> For the minimum viable version, start with
> [Draw in the world](howto/draw-in-world.md) and come back here when you need batching
> or culling.


Nothing in shapez 2 is a `GameObject` per building — at this scale it could not be. The
world is drawn every frame from the model by a pipeline of drawers, using instanced and
combined meshes. To draw something of your own you join that pipeline.

## The pipeline

```text
DrawManager                     owns the frame
├── DrawOptions : FrameDrawOptionsNoLOD
├── Hooks       : DrawHooks     [Obsolete] but simple
└── MapDrawer
    └── Draw(FrameDrawOptionsNoLOD options)
        ├── Culler.Cull(...)          → MapCullResult
        ├── options.Hooks.OnDrawMap(options, map, cullResult)
        ├── foreach (IMapSubDrawer d in SubDrawers)   ← the intended extension point
        │       d.Draw(options, cullResult)
        └── options.Hooks.OnDrawFinal(options)
```

`MapDrawer` catches and logs exceptions from each sub-drawer, so a throwing drawer
degrades rather than killing the frame. It also registers on the map's
island/building add and remove events to invalidate its caches.

## `FrameDrawOptionsNoLOD`

The per-frame context handed to every drawer. Most of what you need is on it:

```csharp
public readonly RenderersCollection Renderers;   // where you submit draws
public readonly Player Player;                   // → Player.CurrentMap
public readonly Viewport Viewport;
public readonly IShapeColorScheme ColorScheme;
public readonly IColorVisualizationManager ColorVisualization;
public readonly AccentColorPalette AccentColorPalette;
public readonly VisualTheme Theme;               // [Obsolete] "should be injected"
public readonly DrawHooks Hooks;                 // [Obsolete]

public float3 CameraPosition_W { get; }
public Plane[] CameraPlanes { get; }
public double SimulationTime_G { get; }
public float DeltaTime { get; }
public int MaxBuildingIslandLayer { get; }
public FrameBudgetManager FrameBudget { get; }
public LODComputationParameters LODComputation { get; }

public const float OverviewModeZoom = 1500f;
public bool InOverviewMode => Viewport.Zoom > 1500f;
```

`InOverviewMode` is how you make an overlay behave differently zoomed out — the game
uses the same flag to swap detailed island meshes for flat overview planes.

`FrameDrawOptions` is the same thing with LOD resolved; drawers that need level of
detail take that instead.

## Submitting a draw

`RenderersCollection` is a bundle of renderers, each batching a different category:

| Field | Use |
| --- | --- |
| `RegularNonInstanced` | arbitrary one-off meshes — the general-purpose choice |
| `UINonInstanced` | the same, on the UI layer |
| `Misc`, `Buildings`, `Islands`, `Playingfield`, `Shapes`, `UI`, … | `InstancedMeshManager`s, for many copies of one mesh |
| `BeltItems` | the item renderer, with shape drawing helpers |
| `LazyMeshCombinationManager` | combined-mesh caching |

The general-purpose call:

```csharp
void DrawMesh(
    IMeshReference mesh,
    IMaterialReference material,
    Matrix4x4 matrix,
    RenderCategory category,
    MaterialPropertyBlock properties = null,
    ShadowToken castShadows = default,
    ShadowToken receiveShadows = default);
```

```csharp
float3 position = tile_G.ToCenter_W(2.0f);

options.Renderers.RegularNonInstanced.DrawMesh(
    mesh,
    material,
    Matrix4x4.TRS(position, Quaternion.identity, new Vector3(20f, 20f, 20f)),
    RenderCategory.Misc,
    MaterialPropertyHelpers.CreateAlphaBlock(0.6f));
```

### Property blocks — read this before using them

```csharp
public static class MaterialPropertyHelpers
{
    public static int SHADER_ID_Alpha     = Shader.PropertyToID("_Alpha");
    public static int SHADER_ID_BaseColor = Shader.PropertyToID("_BaseColor");

    public static MaterialPropertyBlock CreateAlphaBlock(float alpha);
    public static MaterialPropertyBlock CreateBaseColorBlock(Color baseColor);
}
```

Both helpers mutate and return a **shared static** `MaterialPropertyBlock`. That is
fine in the game's usage — set it, submit the draw immediately, never hold it — but it
means you cannot build up several blocks and submit them later, and you cannot combine
alpha and colour by calling both. If you need more than one property at once, keep your
own `MaterialPropertyBlock` instance.

## Meshes

| Type | Lifetime |
| --- | --- |
| `IMeshReference` | the interface everything takes (`InstanceId`, `IsEmpty`, `GetMeshInternal()`) |
| `TemporaryMeshReference` | a mesh you own and **must dispose** |
| `LazyCombinedMesh` | a combined mesh built on demand |
| `ExpiringDisposableObject<T>` | a cache slot that releases after disuse, driven by `IResourceLifetime` |

Ready-made geometry lives on `GeometryHelpers`:

```csharp
public static IMeshReference PlaneMesh;      // a unit plane
public static IMeshReference BillboardMesh;
public static TemporaryMeshReference GeneratePlaneMeshUVColoredUncached(Color color);
public static Mesh GenerateTransformedMeshUncached(Mesh baseMesh, Matrix4x4 trs);
```

Note how the game colours flat overlays: it does **not** set a colour per draw, it bakes
the colour into a plane mesh and caches one mesh per colour
(`HUDOverviewModeMapResourcesRenderer` keeps a `DisposableDictionary<Color,
TemporaryMeshReference>`). For an overlay with a small palette — say eleven buckets from
red to green — that is the pattern to copy.

### Combining

Thousands of individual `DrawMesh` calls will cost you. `MeshBuilder` batches them into
one mesh:

```csharp
using MeshBuilder builder = new MeshBuilder("MyOverlay(chunk)", lod: 0);

foreach (GlobalChunkCoordinate chunk in island.Chunks)
{
    builder.AddTranslateScale(planeMesh, chunk.ToCenter_W(-20f), new float3(20f, 20f, 20f));
}

if (builder.Empty) return null;
TemporaryMeshReference combined = builder.GenerateSingleMeshMax65KVertices();
```

Other adds: `AddTranslate`, `AddByTransform(mesh, in GlobalTileTransform)`,
`AddTranslateRotate(...)`, and LOD-mesh overloads. `GenerateCombined()` and
`GenerateLazy(bool)` are the alternatives when you want a multi-mesh or deferred result.

Build these **once and cache**, keyed by whatever makes them invalid — the game caches
per `SuperChunkCoordinate` and rebuilds when an island or building changes. Rebuilding
a combined mesh every frame is worse than not combining at all.

## Getting your code into the frame

### Option A — postfix `MapDrawer.Draw`

The shortest route, and it hands you the full context:

```csharp
Hook DrawHook = DetourHelper.CreatePostfixHook<MapDrawer, FrameDrawOptionsNoLOD>(
    (drawer, options) => drawer.Draw(options),
    (drawer, options) => Overlay.Draw(options));
```

Your code runs after all the game's drawers, which is what you want for an overlay that
sits on top. `options.Player.CurrentMap` gives you the map without any global lookup.

### Option B — `DrawHooks`

`FrameDrawOptionsNoLOD.Hooks` carries multicast delegates — `OnDrawMap`,
`OnDrawSuperChunk`, `OnDrawShapeResourceSource`, `OnDrawFluidResourceSource`,
`OnDrawIslandNotch`, `OnDrawTrain`, `OnDrawFinal`. `HUDOverviewMode` uses
`OnDrawSuperChunk` this way:

```csharp
DrawHooks hooks = DrawManager.Hooks;
hooks.OnDrawSuperChunk = (DrawHooks.DrawSuperChunkDelegate)Delegate.Combine(
    hooks.OnDrawSuperChunk, new DrawHooks.DrawSuperChunkDelegate(DrawSuperChunk));
```

Always `Delegate.Combine` / `Delegate.Remove`, never assign. The whole class is marked
`[Obsolete("Instead use/introduce concepts like the IMapSubDrawer.")]` — it works, but
it is on the way out.

### Option C — `IMapSubDrawer`

The intended extension point. `DrawManager` takes `IEnumerable<IMapSubDrawer>` at
construction and `MapDrawer` copies it into a list, so registering one after the fact
means reaching that list — which the [publicizer](publicizer.md) makes possible. More
work than a hook, and more idiomatic; worth it for a drawer you intend to maintain.

Sub-drawers can also implement `IMapSubDrawerIslandEventListener` to be told when
islands are registered or unregistered, which is how `IslandOverviewDrawer` keeps its
per-super-chunk cache honest.

## Performance notes

- Cull first. `MapCullResult` from `MapDrawer` tells you which super chunks are visible;
  drawing for the whole map when the camera sees a corner of it is the most common
  mistake.
- Cache combined meshes; invalidate on the map's add/remove events.
- Dispose every `TemporaryMeshReference` — `TemporaryMeshReference.WarnAboutNonDisposedInstances()`
  exists because leaking them is a known trap.
- Respect `options.FrameBudget` if you generate meshes lazily.
