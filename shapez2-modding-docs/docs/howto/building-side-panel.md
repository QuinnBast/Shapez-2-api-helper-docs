# Add a section to the building side panel

**Problem.** You want your own information to appear in the panel that opens when the
player selects a building — ideally reusing the game's own widgets rather than building
UI.

**Solution.** Register an `IBuildingModules` provider. The game asks every registered
provider for "module data" objects, each of which names a prefab the HUD instantiates.
You never touch Unity.

## The contract

```csharp
public interface IBuildingModules
{
    IEnumerable<IHUDSidePanelModuleData> GetInfoModules(IMapModel map, BuildingModel building);
    IEnumerable<IHUDSidePanelModuleData> GetInfoModules(IBuildingDefinition definition);
}
```

Two overloads for two contexts: the `BuildingModel` one for a **placed, selected**
building (live state available), the `IBuildingDefinition` one for a building being
**previewed** during placement (no instance yet).

## Registering it

The game builds a `BuildingsModulesLookup` once per session and calls
`GameSessionOrchestrator.InjectBuildingsModuleProviders(lookup)`. ShapezShifter
intercepts exactly that:

```csharp
using ShapezShifter.Hijack;

public class MyModulesRewirer : IBuildingModulesRewirer
{
    public void AddModules(BuildingsModulesLookup modulesLookup)
    {
        modulesLookup.AddModule(definitionId, buildingDefinition, new MyBuildingModules());
    }
}

// in your IMod constructor:
RewirerHandle handle = GameRewirers.AddRewirer(new MyModulesRewirer());
```

`AddModule(BuildingDefinitionId variant, IBuildingDefinition definition, IBuildingModules provider)`
attaches your provider to one building type.

If you are *adding* a building through Flow, do not do any of this by hand — use the
modules stage of the builder chain instead:

```csharp
.WithCustomModules(myBuildingModules)          // or
.WithAtomicShapeProcessingModules(BuiltinResearchSpeed.CutterSpeed, 2.0f)
```

See [Add a building](add-a-building.md#the-whole-chain). Registering a rewirer by hand
is for adding panels to **buildings that already exist**.

## The modules you can return

Each is a nested `Data` class implementing `IHUDSidePanelModuleData`:

| Module | Data constructor | Shows |
| --- | --- | --- |
| `HUDSidePanelModuleInfoText` | `Data(IText text)` | a line of text |
| `HUDSidePanelModuleGenericSection` | `Data(Sprite icon, IText text, IText description)` | an icon + title + description block |
| `HUDSidePanelModuleStats` | `Data(params StructureStat[] stats)` | the stats rows vanilla uses |
| `HUDSidePanelModuleBuildingEfficiency` | `Data(building, simulation, targetLane, speedId, duration)` | the efficiency meter |
| `HUDSidePanelModuleBeltItemContents` | `Data(IItemSimulation simulation)` | what is on the lanes |
| `HUDSidePanelModuleActionButton` | `Data(IEnumerable<PlacementKeybindingHintData>)` | action buttons with key hints |
| `HUDSidePanelModuleGenericButton` | — | a button |
| `HUDSidePanelModuleDropdownSelect` | — | a dropdown |
| `HUDSidePanelModuleFluidContainer` | — | fluid contents |
| `HUDSidePanelModuleRecipeMatrix` | — | a recipe grid |
| `HUDSidePanelModuleStructureOverview` | `Data(overview, speedId, duration)` | the structure overview block |

`HUDSidePanelModuleInfoText` and `HUDSidePanelModuleGenericSection` cover most custom
needs and cost one line each.

## The simplest useful provider

```csharp
public class MyBuildingModules : IBuildingModules
{
    public IEnumerable<IHUDSidePanelModuleData> GetInfoModules(IMapModel map, BuildingModel building)
    {
        yield return new HUDSidePanelModuleInfoText.Data(
            new RawText($"Tile: {building.Tile_G}"));
    }

    public IEnumerable<IHUDSidePanelModuleData> GetInfoModules(IBuildingDefinition definition)
    {
        yield break;   // nothing during placement preview
    }
}
```

Ship translation keys rather than `RawText` for anything a player reads
([translations](add-translations.md)).

## Reaching the live simulation

For state-driven panels, derive from
`SimulationBasedBuildingModuleDataProvider<TLocalized, TSimulation>` and the base
resolves the simulation for you:

```csharp
public class MyModules : SimulationBasedBuildingModuleDataProvider<ILocalizedTileSimulation, IItemSimulation>
{
    protected override IEnumerable<IHUDSidePanelModuleData> GetSimulationModules(
        BuildingModel building,
        ILocalizedTileSimulation localizedSimulation,
        IItemSimulation itemSimulation)
    {
        yield return new HUDSidePanelModuleBeltItemContents.Data(itemSimulation);
    }

    public override IEnumerable<StructureStat> GetStats(IBuildingDefinition definition)
    {
        yield return new StructureStatProcessingTime(duration, speedId, outputLanes);
    }
}
```

That is exactly how `ItemSimulationBuildingModuleDataProvider` works — worth reading in
full, since it is the closest thing to a reference implementation. It also shows the
efficiency module being wired to either the input or the output lane via
`ItemSimulationEfficiencyMeasurementMode`.

## Adding to a vanilla building's panel

`AddModule` attaches a provider to a definition id, and the lookup asks **all**
registered providers, so your provider adds to the panel rather than replacing what is
already there. To decorate a vanilla cutter, register against the cutter's definition id
— get the real id from `debug.export-game-data` in the `F1` console.

## Gotchas

- **Modules are data, not views.** Return the `Data` object; the HUD instantiates the
  prefab. Do not try to build a `GameObject`.
- The two `GetInfoModules` overloads run in different contexts. Returning
  instance-dependent data from the definition overload has nothing to read state from.
- Providers are asked **every time the panel opens**. Keep them allocation-light and do
  no scanning — the panel is on the player's critical path.
- `iterator` methods (`yield return`) are lazy: exceptions surface when the HUD
  enumerates, not when your method is called, which makes them look like HUD bugs.
- Dispose the `RewirerHandle` in `IMod.Dispose()`.
