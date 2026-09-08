# Architecture

Two things to hold in your head: how the *game* is split into assemblies, and how
*ShapezShifter* is split into layers. Pick the wrong layer and you will fight the API
for days; pick the right one and most mods are a few hundred lines.

## ShapezShifter's three layers

ShapezShifter is deliberately stacked from "convenient" down to "unlimited". Always
start at the top and drop a layer only when the one above cannot express what you want.

```text
┌───────────────────────────────────────────────────────────┐
│ Flow        fluent builders — buildings, islands,         │  ← start here
│             research, toolbar, console commands           │
├───────────────────────────────────────────────────────────┤
│ Hijack      interceptors + rewirers — extend a game       │
│             structure at a defined extension point        │
├───────────────────────────────────────────────────────────┤
│ SharpDetour hooks — prefix/postfix/replace any method     │  ← unlimited, brittle
└───────────────────────────────────────────────────────────┘
```

### Flow — `ShapezShifter.Flow`

Fluent builders for *adding content*. If your mod adds a building, an island, a
research side-upgrade, a toolbar entry, or a console command, this is the whole API you
need:

```csharp
IBuildingBuilder builder = Building.Create(new BuildingDefinitionId("DiagonalCutter"))
   .WithConnectorData(connectorData)
   .DynamicallyRendering<Renderer, Simulation, RendererData>(new DiagonalCutterDrawData())
   .WithStaticDrawData(drawData)
   .WithoutSound()
   .WithoutSimulationConfiguration()
   .WithEfficiencyData(new BuildingEfficiencyData(2.0f, 1));
```

Flow also carries `ShapezShifter.Kit`, a grab-bag of helpers that are useful from any
layer: `ModDirectoryLocator` / `ModFolderLocator` (find your own files at runtime),
`AssetBundleHelper`, `FileTextureLoader`, `FileMeshLoader`, `LodMeshBuilder`,
`MaterialHelper`, `ShaderHelper`, and `GameHelper`.

### Hijack — `ShapezShifter.Hijack`

Interception points for structures the game builds once per session. The pattern is
always the same pair: an **interceptor** catches the game mid-construction, and a
**rewirer** you register says what to add.

Interceptors that exist today:

| Interceptor | Extension point |
| --- | --- |
| `BuildingsInterceptor` / `IBuildingsRewirer` | building definitions |
| `IslandsInterceptor` / `IIslandsRewirer` | island definitions |
| `BuildingModulesInterceptor` / `IBuildingModulesRewirer` | HUD side-panel modules for a selected building |
| `IslandModulesInterceptor` / `IIslandModulesRewirer` | the same, for islands |
| `SimulationSystemsInterceptor` / `ISimulationSystemsRewirer` | systems in the simulation loop |
| `PredictionSystemsInterceptor` / `IPredictionSystemsRewirer` | the prediction/preview simulation |
| `PlacementInitiatorsInterceptor` / `I*PlacementRewirers` | placers, per entity family |
| `ToolbarInterceptor` / `IToolbarDataRewirer`, `IToolbarModelRewirer` | toolbar contents |
| `GameScenarioInterceptor` / `IGameScenarioRewirer` | scenario/game-data |
| `SaveDataInterceptor` / `ISaveDataRewirer` | extra blobs in the savegame |
| `ConsoleInterceptor` / `IConsoleRewirer` | dev console commands |
| `BuffablesInterceptor` / `IBuffablesRewirer` | buffs/speed upgrades |
| `TickInterceptor` / `ITickRewirer` | a per-frame callback |

Rewirers are registered with `GameRewirers.AddRewirer(...)`, which hands back a
`RewirerHandle` you can dispose. Most of the time you will not construct an interceptor
yourself — Flow does it for you, and for ticks there is a one-line extension
(see [Mod Lifecycle](mod-lifecycle.md#per-frame-work)).

### SharpDetour — `ShapezShifter.SharpDetour`

A [MonoMod](https://github.com/MonoMod/MonoMod) wrapper for patching arbitrary methods.
Everything else is built on it. You express the target method as a *lambda* rather than
a reflection string, so it is compile-time checked and survives renames:

```csharp
Hook hook = DetourHelper.CreatePostfixHook<MapDrawer, FrameDrawOptionsNoLOD>(
    (drawer, options) => drawer.Draw(options),   // which method
    (drawer, options) => MyOverlay.Draw(options) // what to run after it
);
```

Full details and the available overload shapes are in
[Hooking the Game](hooking.md).

## Assembly map

The game is split into roughly fifty assemblies. These are the ones a mod usually
touches:

| Assembly | Contains |
| --- | --- |
| `SPZGameAssembly` | The big one: HUD, rendering, drawers, building/island definitions, statistics, `Globals` |
| `Game.Core` | Engine-level odds and ends — keybindings, preferences |
| `Game.Core.Coordinates` | Every coordinate and transform type ([Coordinates](coordinates.md)) |
| `Game.Core.Map.Model` | `IMapModel`, `IslandModel`, `BuildingModel` ([Map Model](map-model.md)) |
| `Game.Core.Map.Layout.Model` | `IslandInstanceModel`, `BuildingInstanceModel` — the stored instance data behind the models |
| `Game.Core.Map.Simulation` | `ISimulator`, `ILocalizedSimulation`, `SimulationStateContainer`, `ICustomDataReader` |
| `Game.Core.Simulation` | `Ticks`, `Steps`, simulation buffers and constants |
| `Game.Content` | Concrete machines: cutters, painters, rotators, stackers, trains, fluids |
| `Game.Content.Features` | The item/lane model (`IItemLane`, `BeltLane`) and the generic simulation systems |
| `Game.Core.Rendering` | Rendering primitives shared by the drawers |
| `Game.Orchestration` | `GameSessionOrchestrator` — wires a play session together |
| `Game.Interaction` | Placement, blueprints, player interaction |
| `Game.Hud`, `Game.Core.HUD` | HUD framework types |
| `Core`, `Core.Localization` | Logging, events, collections, `IText` / `.T()` translation |

A rough rule: **`Game.Core.*` is model and framework, `Game.Content*` is the actual
machines, and `SPZGameAssembly` is presentation** — HUD and rendering. If you cannot
find a type, it is probably in `SPZGameAssembly`.

## How a session fits together

`GameSessionOrchestrator` (in `Game.Orchestration`) is the object that owns a running
game. Its fields are a useful table of contents for the whole engine —
`MapModel`, `Simulator`, `Draw` (a `DrawManager`), `HUD`, `Research`, `Savegame`,
`SimulationSpeed`, `MapLayout`, and so on.

The pieces a mod interacts with most:

```text
GameSessionOrchestrator
├── MapModel      (IMapModel)  islands, buildings, layout   → map-model.md
│   └── Simulator (ISimulator) the running machines          → simulations-and-lanes.md
├── Draw          (DrawManager) per-frame draw pipeline      → rendering.md
└── HUD           side panels, dialogs, overlays
```

`GameSessionOrchestrator.Tick(float)` is the per-frame pulse; ShapezShifter's
`TickInterceptor` postfixes exactly that method to drive `ITickRewirer`.
