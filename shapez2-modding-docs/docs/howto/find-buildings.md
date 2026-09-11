# Find buildings

## Every building of a given type

```csharp
private static readonly BuildingDefinitionId CutterId = new BuildingDefinitionId("Cutter");

private static void FindAllCutters(IMapModel map, List<BuildingModel> results)
{
    results.Clear();

    foreach (BuildingModel building in map.Buildings)
    {
        if (building.Definition.Id == CutterId)
        {
            results.Add(building);
        }
    }
}
```

To get the real ids, dump them once rather than guessing:

```csharp
foreach (BuildingModel building in map.Buildings)
{
    Logger.Info?.Log(building.Definition.Id.ToString());
}
```

### Finding definition ids

Two ways, and the first is easier:

**`debug.export-game-data`** — press `F1` in game and run it. The game writes its
content out as JSON, definition ids included. This is also where milestone ids and
scenario data come from.

**Log them at runtime** — useful when you want only what is actually placed:

```csharp
foreach (BuildingModel building in map.Buildings)
{
    Logger.Info?.Log(building.Definition.Id.ToString());
}
```

## Per island instead of the whole map

Usually what you actually want — it keeps the working set small and gives you somewhere
to attribute results:

```csharp
List<BuildingModel> buildings = new List<BuildingModel>();

foreach (IslandModel island in map.Islands)
{
    buildings.Clear();
    island.GetBuildings(buildings);   // reuses your list — no allocation

    foreach (BuildingModel building in buildings)
    {
        // …
    }
}
```

Prefer `island.GetBuildings(list)` over the `island.Buildings` property in any loop that
runs repeatedly: the property is an iterator that rents a scoped list on every call.

## The building at a tile

```csharp
if (map.TryGetBuilding(tile_G, out BuildingModel building))
{
    // …
}
```

Or island-relative, when you already have the island:

```csharp
if (island.TryGetBuilding(in tile_I, out BuildingModel building))
{
    // …
}
```

Use the `TryGet…` forms. The `Get…` forms throw when nothing is there.

## Buildings in a region

```csharp
foreach (BuildingId id in map.GetChunkBuildings(chunk_GC))
{
    if (map.TryGetBuilding(id, out BuildingModel building))
    {
        // …
    }
}
```

`GetChunkBuildings` yields ids, not models — a multi-tile building spanning two chunks
appears in both, so de-duplicate by `BuildingId` if that matters to you.

## Reacting to buildings appearing and disappearing

Instead of rescanning, subscribe:

```csharp
map.OnBuildingAdded.Register(OnBuildingAdded);
map.OnBeforeBuildingRemoved.Register(OnBuildingRemoved);

// and the island equivalents:
map.OnIslandAdded.Register(OnIslandAdded);
map.OnBeforeIslandRemoved.Register(OnIslandRemoved);
```

Note the asymmetry: added fires *after*, removed fires *before*. In the remove handler
the building is still valid, so you can read it one last time — which is your only
chance to clean up anything keyed by it.

Unregister these when the map goes away
([Run code when a game loads](run-code-when-game-loads.md)).

## Gotchas

- **Do not cache `BuildingModel` long-term.** It is a struct handle; when the building is
  deleted it goes stale rather than null. Cache `BuildingId` and look it up again.
- `BuildingModel.IsValid` does a full map lookup on every read. Use it once to check a
  stored handle, never inside a loop.
- `map.Buildings` on a large save is tens of thousands of entries. Pace the scan — see
  [Run code when a game loads](run-code-when-game-loads.md#do-not-do-heavy-work-every-tick).
