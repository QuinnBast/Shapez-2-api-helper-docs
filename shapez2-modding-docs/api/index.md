# API Reference

Generated with [DocFX](https://dotnet.github.io/docfx/) directly from the shipped
shapez 2 assemblies and `ShapezShifter.dll`. Use the tree on the left to browse by
namespace, or the search box for a type or member name.

## What is covered

The modding-relevant assemblies only — roughly 5,000 types:

`SPZGameAssembly` · `Game.Content` · `Game.Content.Features` ·
`Game.Content.Rendering` · `Game.Core` · `Game.Core.Coordinates` · `Game.Core.HUD` ·
`Game.Core.Map` · `Game.Core.Map.Model` · `Game.Core.Map.Simulation` ·
`Game.Core.Map.Layout` · `Game.Core.Map.Layout.Model` · `Game.Core.Modding` ·
`Game.Core.Rendering` · `Game.Core.Simulation` · `Game.Core.Serialization` ·
`Game.Hud` · `Game.Interaction` · `Game.Logic` · `Game.Modding` ·
`Game.Orchestration` · `Core` · `Core.Localization` · `ShapezShifter`

Achievements, analytics, platform/Steam integration, and editor tooling are left out.
To add one, put it in `metadata.src` in `docfx.json` and regenerate.

## The `Root` namespace

A large share of shapez 2's types — `BuildingModel`, `IMapModel`, `MapDrawer`,
`FrameDrawOptionsNoLOD`, `IItemLane`, most of the HUD — are declared in the **global
namespace**, with no `namespace` statement at all. They need no `using` in your code.

Documentation tooling has to call that namespace *something*, so here it is listed as
**`Root`**. `Root.BuildingModel` on this site is plain `BuildingModel` in source. It is
a display name only, invented by these docs; do not write it in code.

If a type seems to be missing, look for it under `Root` before assuming it is not
covered.

## What is missing, and why

The game ships **no XML documentation comments**, so every member here shows its
signature with no description. That is not a generation failure — the information does
not exist in the assemblies.

So use this reference for what it is good at:

- exact signatures, including optional and `in` parameters
- inheritance chains and interface implementations
- finding every type in a namespace you half-remember

For *why* you would call something, see the [Guide](../docs/index.md). For *how the
game itself uses it*, decompile locally — see
[Exploring the Assemblies](../docs/exploring-assemblies.md).

## Good entry points

| Type | Why |
| --- | --- |
| `IMapModel` | the root of everything placed in the world |
| `IslandModel`, `BuildingModel` | the handles you will pass around constantly |
| `ISimulator`, `IItemSimulation`, `IItemLane` | what machines are actually doing |
| `FrameDrawOptionsNoLOD`, `RenderersCollection` | everything needed to draw |
| `ShapezShifter.SharpDetour.DetourHelper` | patching arbitrary methods |
| `ShapezShifter.Flow.Building`, `…Flow.Island` | adding content |
