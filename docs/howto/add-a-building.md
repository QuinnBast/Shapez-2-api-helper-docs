# Add a building

**Problem.** You want a new machine in the game — placeable, researchable, on the
toolbar, with its own simulation.

**Solution.** `AtomicBuildings.Extend()`. The fluent chain is a **state machine**: each
stage returns a different interface, so the compiler enforces the order and refuses to
let you skip a required step. That is the single most useful thing to know about it —
if a method you expect is missing, you are at the wrong stage, not missing a using.

## The whole chain

```csharp
AtomicBuildings.Extend()
   .AllScenarios()                                        // which scenarios
   .WithBuilding(buildingBuilder, buildingGroupBuilder)   // what it is
   .UnlockedWithNewSideUpgrade(sideUpgradeBuilder)        // how it is researched
   .WithDefaultPlacement()                                // how it is placed
   .InToolbar(toolbarLocation)                            // where it appears
   .WithSimulation(new MyFactoryBuilder(), logger)        // what it does
   .WithAtomicShapeProcessingModules(BuiltinResearchSpeed.CutterSpeed, 2.0f)  // HUD panel
   .WithPrediction(new MyPredictionFactoryBuilder(), logger)                  // preview
   .Build();
```

Stage by stage, with the interface that each returns:

| Call | Returns | Alternatives at this stage |
| --- | --- | --- |
| `AtomicBuildings.Extend()` | scenario selector | — |
| `.AllScenarios()` | `IScenarioSelectiveBuildingExtender` | per-scenario selection |
| `.WithBuilding(b, group)` | `IDefinedBuildingExtender` | — |
| `.UnlockedWithNewSideUpgrade(u)` | `IDefinedUnlockableBuildingExtender` | `UnlockedAtMilestone`, `UnlockedWithExistingSideUpgrade` — see [research](add-research-unlock.md) |
| `.WithDefaultPlacement()` | `IDefinedPlaceableBuildingExtender` | skip it and go straight to `WithSimulation` |
| `.InToolbar(loc)` | `IDefinedPlaceableAccessibleBuildingExtender` | see [toolbar](add-to-toolbar.md) |
| `.WithSimulation(f, logger)` | `IAtomicBuildingExtender` | — |
| `.WithAtomicShapeProcessingModules(id, d)` | `IViewModularBuildingExtender` | `WithCustomModules(...)` |
| `.WithPrediction(f, logger)` | `IBuildingExtender` | — |
| `.Build()` | `void` | — |

Nothing happens until `Build()`.

## The two builders it needs

### The group

A group is the toolbar/HUD-level grouping a building belongs to — its icon, title, and
placement behaviour:

```csharp
BuildingDefinitionGroupId groupId = new("MyCutterGroup");

ModFolderLocator resources = ModDirectoryLocator.CreateLocator<MyMod>().SubLocator("Resources");

IBuildingGroupBuilder group = BuildingGroup.Create(groupId)
   .WithTitle("my-mod.cutter.title".T())
   .WithDescription("my-mod.cutter.description".T())
   .WithIcon(FileTextureLoader.LoadTextureAsSprite(resources.SubPath("Icon.png"), out _))
   .AsNonTransportableBuilding()
   .WithPreferredPlacement(DefaultPreferredPlacementMode.LinePerpendicular)
   .WithDefaultStructureOverview();
```

`.T()` resolves a translation key — see [Add translations](add-translations.md).

### The building

```csharp
BuildingDefinitionId definitionId = new("MyCutter");

IBuildingConnectorData connectors = BuildingConnectors.SingleTile()
   .AddShapeInput(ShapeConnectorConfig.DefaultInput())
   .AddShapeOutput(ShapeConnectorConfig.DefaultOutput())
   .Build();

IBuildingBuilder building = Building.Create(definitionId)
   .WithConnectorData(connectors)
   .DynamicallyRendering<MyRenderer, MySimulation, IMyDrawData>(new MyDrawData())
   .WithStaticDrawData(CreateDrawData(resources))
   .WithoutSound()
   .WithoutSimulationConfiguration()
   .WithEfficiencyData(new BuildingEfficiencyData(2.0f, 1));
```

`BuildingConnectors.SingleTile()` has a multi-tile counterpart
(`IMultiTileConnectorDataBuilder`) for buildings larger than one tile. Inputs and
outputs come in shape, fluid, and signal flavours.

`WithEfficiencyData(new BuildingEfficiencyData(baseProcessingDuration, laneCount))` is
what makes the vanilla efficiency panel work for your building — pass the same duration
your simulation actually uses, or the readout lies.

## The pieces you still have to write

The chain wires things up; these are yours:

| Piece | What it is | Sample |
| --- | --- | --- |
| Simulation | the per-instance logic, `Simulation<TState>` | `DiagonalCutterSimulation` |
| Simulation state | the serialised fields, `ISimulationState` | `DiagonalCutterSimulationState` |
| Factory builder | tells the game how to construct the simulation | `DiagonalCutterFactoryBuilder` |
| Renderer | draws the live state | `DiagonalCutterSimulationRenderer` |
| Draw data | static meshes and rendering config | `DiagonalCutterDrawData` |
| Prediction factory | drives the placement preview | `Operation1In1OutPredictionFactoryBuilder` |

See [Simulations and Item Lanes](../simulations-and-lanes.md) for how a simulation is
built out of lanes, and copy the `DiagonalCutter` sample rather than starting blank —
it is the only complete worked example of all six.

## Gotchas

- **Ids are permanent.** `BuildingDefinitionId` ends up in save files. Renaming one
  breaks every save that placed your building.
- Build the whole chain in your `IMod` constructor. Content registration happens at mod
  load, not per session.
- `WithoutSimulationConfiguration()` is a real choice, not boilerplate — use it only
  when the building has nothing configurable per instance.
- Loading meshes and icons is its own small mess:
  [Load models and icons](load-models-and-icons.md).
