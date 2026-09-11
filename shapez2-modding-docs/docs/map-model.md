# The Map Model

> **You need this when** you are walking islands or buildings, attributing something to
> a platform, or asking "where does this machine's state actually live?".
> Task-shaped versions: [Find buildings](howto/find-buildings.md) and
> [Read machine state](howto/read-machine-state.md).


Everything placed in the world hangs off `IMapModel`. The hierarchy is three deep:

```text
IMapModel                     the world
└── IslandModel               a platform / island        (struct)
    └── BuildingModel         a machine on that island   (struct)
        └── SimulationStateContainer   its persisted state
```

`IslandModel` and `BuildingModel` are **structs** — cheap handles that carry an id, a
reference back to the map, and an instance record. Passing them around costs nothing;
storing them long-term is a mistake, because a handle to a deleted entity does not
become `null`, it becomes stale. Store the `IslandId` / `BuildingId` instead and look
it up again.

## `IMapModel`

```csharp
public interface IMapModel :
    IMapLayers, IIslandModelAccessor, IBuildingModelAccessor,
    ISuperChunkAccessor, IBunchEditor
{
    IMapLayout Layout { get; }
    ISimulator Simulator { get; }
    IGameResourcesAccessor ResourcesAccessor { get; }

    BuildingModel CreateBuilding(IBuildingDefinition definition, in GlobalTileTransform transform, IBuildingConfiguration configuration);
    void DeleteBuilding(in BuildingId buildingId);
    IslandModel CreateIsland(IIslandDefinition definition, GlobalChunkTransform transform, IIslandConfiguration configuration);
    void DeleteIsland(in IslandId islandId);

    void GetIslandBuildings(IslandId islandId, ICollection<BuildingModel> buildings);
    IEnumerable<BuildingId> GetChunkBuildings(GlobalChunkCoordinate chunk_GC);
    int GetBuildingsCount(IslandId id);
}
```

`Simulator` is the bridge from "a thing exists here" to "a thing is *doing* something" —
see [Simulations and Item Lanes](simulations-and-lanes.md).

Enumeration and lookup come from `IIslandModelAccessor`:

```csharp
IEnumerable<IslandModel> Islands { get; }
int IslandCount { get; }
IEvent<IslandModel> OnIslandAdded { get; }
IEvent<IslandModel> OnBeforeIslandRemoved { get; }

bool TryGetIsland(IslandId islandId, out IslandModel island);
bool TryGetIsland(GlobalChunkCoordinate position, out IslandModel island);
bool TryGetIsland(GlobalTileCoordinate position, out IslandModel island);
bool HasIslandAt(GlobalChunkCoordinate position);
IslandModel GetIsland(IslandId islandId);   // throws if absent
```

`IBuildingModelAccessor` mirrors this for buildings, including `Buildings`,
`TryGetBuilding`, `OnBuildingAdded`, and `OnBeforeBuildingRemoved`.

The four add/remove events are how the game's own caches stay current — `MapDrawer`
registers on all of them to invalidate combined meshes. If you cache anything per
island or per building, do the same.

## `IslandModel`

```csharp
public readonly struct IslandModel : IEquatable<IslandModel>
{
    public IslandId Id { get; }
    public IMapModel Map { get; }
    public IIslandDefinition Definition { get; }
    public IIslandConfiguration Configuration { get; }
    public IslandDefinitionId DefinitionId { get; }

    public GlobalChunkTransform Transform { get; }
    public GlobalChunkCoordinate Position { get; }
    public GridRotation Rotation { get; }
    public GlobalChunkBounds Bounds { get; }
    public IEnumerable<GlobalChunkCoordinate> Chunks { get; }

    public int BuildingsCount { get; }
    public IEnumerable<BuildingModel> Buildings { get; }
    public void GetBuildings(List<BuildingModel> buildings);
    public BuildingModel GetBuilding(in IslandTileCoordinate tile_I);
    public bool TryGetBuilding(in IslandTileCoordinate tile_I, out BuildingModel building);

    public SimulationStateContainer State { get; }
}
```

Islands are measured in **chunks**; buildings are measured in **tiles**
([Coordinates](coordinates.md)). `Chunks` walks every chunk the island occupies, which
is what you want for drawing anything that covers a whole platform.

Two allocation notes:

- `Buildings` is an iterator that internally rents a scoped list — fine to `foreach`,
  wasteful in a tight loop. When you are sweeping every island every frame, prefer
  `GetBuildings(List<BuildingModel>)` with a list you own and clear.
- `LayoutQuery` is marked `[Obsolete("This is very inconvenient. Check alternative
  usages (e.g. Chunks).")]` and allocates a query object per call. Use `Chunks` or
  `Bounds`.

## `BuildingModel`

```csharp
public readonly struct BuildingModel : IEquatable<BuildingModel>
{
    public readonly BuildingId Id;
    public readonly IMapModel Map;
    public bool IsValid { get; }               // re-checks the map — see below

    public IBuildingDefinition Definition { get; }
    public IBuildingConfiguration Configuration { get; }
    public IslandModel Island { get; }

    public GlobalTileTransform Transform { get; }
    public IslandTileTransform Transform_I { get; }
    public GlobalTileCoordinate Tile_G { get; }
    public IslandTileCoordinate Tile_I { get; }
    public GlobalTileBounds Bounds_G { get; }
    public IEnumerable<GlobalTileCoordinate> Tiles_G { get; }
    public GridRotation Rotation_G { get; }

    public int NumConnectors { get; }
    public BuildingConnector GetConnector(int connectionIndex);
    public BuildingConnection GetConnection(int connectionIndex);

    public SimulationStateContainer State { get; }
}
```

`IsValid` is not a cheap flag — it does a map lookup every time you read it. Call it
once when you are unsure whether a stored handle is still live, not in a loop.

`Tiles_G` yields every tile a multi-tile building covers, already rotated into world
orientation. Use it whenever you want to cover a building's footprint rather than just
its origin tile.

## Definitions vs. configurations vs. state

Three different things, easily confused:

| | Scope | Example |
| --- | --- | --- |
| **Definition** (`IBuildingDefinition`) | one per building *type* | the cutter's connector layout, its meshes, its base processing duration |
| **Configuration** (`IBuildingConfiguration`) | per placed instance, chosen at placement | which shape a miner filters for |
| **State** (`SimulationStateContainer`) | per placed instance, changes every tick | what item is on the input lane, how far along it is |

### Custom data on a definition

Definitions carry a bag of attached data objects, read through `ICustomDataReader`:

```csharp
public interface ICustomDataReader
{
    IReadOnlyList<object> All { get; }
    TData Get<TData>();                       // throws if absent
    bool Has<TData>();
    bool TryGet<TData>(out TData typedData);
    TData GetOrDefault<TData>(TData defaultData);
}
```

This is how rendering data, sound definitions, connector data, and efficiency data are
all attached to a building type without bloating one class. Lookup matches by
*assignable* type and caches the result, distinguishing "no match", "one match", and
"several matches" — so asking for an interface works, but asking for one that several
attached objects implement will not resolve to a single value.

```csharp
if (building.Definition.CustomData.TryGet<IBuildingEfficiencyData>(out var efficiency))
{
    float baseDuration = efficiency.OriginalProcessingDuration;
    int lanes = efficiency.ProcessingLaneCount;
}
```

> [!NOTE]
> `BuildingEfficiencyData` is attached to *every* building definition by
> `BuildingDefinitionFactory`, including belts. Its presence does not mean "this is a
> processing machine" — check the value, not the existence.

Your own Flow-built buildings get theirs from `.WithEfficiencyData(...)`.

### Reading state

`SimulationStateContainer` is a typed one-slot box:

```csharp
public bool Is<T>() where T : class, ISimulationState;
public bool Is<T>(out T state) where T : class, ISimulationState;
public T As<T>() where T : class, ISimulationState;   // null if wrong type
public T Cast<T>() where T : class, ISimulationState; // throws if wrong type
public T New<T>() where T : class, ISimulationState, new();
```

```csharp
if (building.State.Is<HalfCutterSimulationState>(out var cutterState))
{
    // the persisted lane states live here
}
```

State is what gets serialized into the save. For reading what a machine is doing *right
now*, the live simulation object is usually the better source —
see [Simulations and Item Lanes](simulations-and-lanes.md).

## Walking the world

```csharp
IMapModel map = GameHelper.Core?.LocalPlayer?.CurrentMap;
if (map == null) return;

List<BuildingModel> buildings = new List<BuildingModel>();

foreach (IslandModel island in map.Islands)
{
    buildings.Clear();
    island.GetBuildings(buildings);

    foreach (BuildingModel building in buildings)
    {
        // building.Definition.Id, building.Tile_G, building.State …
    }
}
```

On a large save this loop is thousands of buildings. Anything you do inside it happens
thousands of times — see [Mod Lifecycle](mod-lifecycle.md#per-frame-work) for pacing it.
