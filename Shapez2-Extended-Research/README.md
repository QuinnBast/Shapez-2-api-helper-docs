# Extended Research

More research past the point where vanilla stops. Nothing new is invented — the mod adds
further tiers to upgrades the game already has, and further layers to limits the game
already expresses as data.

## What it adds

| | |
|---|---|
| **Production speed** | +5 tiers on `LRUGlobalSpeed`, the single upgrade driving all machine speed |
| **Train speed** | +5 tiers |
| **Train capacity** | +3 tiers |
| **Platform capacity** | +5 tiers on `LRUChunkLimitAdd` — see the note below |
| **Machine levels** | **off** — not possible on stock art, see [Why layers are capped](#why-layers-are-capped) |
| **Space levels** | **off by default** — works, but space belts cannot reach every new layer |

The upgrade tiers continue vanilla's own final increment, so if the last shipped step was
+25% then so is every step this adds. Costs compound at 1.6× per tier. The layer unlocks
appear in a new **Extended** shop category, gated behind the last vanilla layer of their
kind and then chained so they are bought in order.

All of it is configurable — see [Configuration](#configuration).

## Why this is a small mod

Both layer limits are counts of a list, not constants:

```csharp
// GameMode.cs
public short MaxBuildingLayer => (short)Mechanics.BuildingLayerUnlocks.Count;
public short MaxIslandLayer   => (short)(MinIslandLayer + Mechanics.IslandLayerUnlocks.Count);
```

A layer exists because a research mechanic exists to unlock it. Adding a layer means adding
a mechanic and giving the player a way to earn it, which is all `LayerUnlockExtender` does.

Linear upgrades are similar — a list of `(value, cost)` rows behind
`ResearchLinearUpgrade`. The only wrinkle is that each row also caches the *next* row's
value and cost, so appending means rebuilding the list rather than pushing onto it. That is
`LinearUpgradeExtender`.

The whole mod is one `IGameScenarioRewirer`, which ShapezShifter runs immediately after the
game builds a `GameScenario`. There is no simulation, no rendering, and no hook on the tick.

## Nothing is hardcoded to a vanilla id

The upgrades to extend are discovered from the roles the scenario declares — the
speed-to-upgrade mapping, and the named `ChunkLimitAddUpgrade` / `HubInputSize` /
`ShapeQuantityUpgrade` fields — rather than from a list of ids baked into the mod. A game
update that renames an upgrade or adds a sixth speed gets picked up instead of missed.

Every upgrade the mod touches is logged with its id, which is also how you find the ids to
write overrides against:

```
Extended Research: 'BeltSpeedUpgrade' +5 tiers (now 12 max, value up to 350).
```

## Configuration

Optional. Copy `config.example.json` to `config.json` in the mod folder and edit; every
value in the example is the built-in default. A missing or malformed file falls back to
defaults rather than failing.

Two knobs worth knowing:

- **`ValueStepMultiplier`** (default `1.0`) scales the per-tier reward. Leaving it at 1.0
  keeps the game's own pace — the costs are usually the dial you want to move.
- **`Overrides`** sets tier counts per upgrade id, beating every category default. Use the
  ids from the log.

**Platform capacity is the one worth retuning.** Vanilla's last chunk-limit step is already
enormous, so continuing it linearly runs the cap up into the millions — technically correct
and practically meaningless. Set `ExtraPlatformCapacityTiers` to `0`, or add an override on
`LRUChunkLimitAdd` with a `ValueStepMultiplier` you like, if that bothers you.

A shop entry's preview image is **not** optional: `HUDResearchSideUpgradeDisplay.RebuildView`
calls `GameData.GetImage` unconditionally and it throws on an unresolvable id, taking the
whole research screen down with it. The mod borrows an image id from an existing side upgrade
rather than passing `GameImageId.Empty`, and skips the layer unlocks entirely if it cannot
find one.

## Why layers are capped

**Machine levels cannot be added at all.** Three building layers exist because three sets of
meshes exist, and vanilla already uses all three:

```csharp
// BuildingDrawDataFactory.FromMeta
int num = 3;
ILODMesh[] array = new ILODMesh[num];
// StaticBuildingMeshBuilder.BuildBaseMesh
... .MainMeshPerLayer[tile_G.BuildingLayer()]
```

A building on layer 3 throws `IndexOutOfRangeException` out of `BuildBaseMesh`, which aborts
the mesh build for its entire chunk — so the building is placeable, simulates fine, and is
completely invisible along with everything near it. `IslandLayoutFactory` hardcodes 3 as well.
Valid z is `0..MaxBuildingLayer` and `MaxBuildingLayer` is `BuildingLayerUnlocks.Count`, so
that count has to stay at 2, which is what vanilla ships. `ExtraBuildingLayers` is clamped to
zero and only becomes useful alongside a mod that authors a fourth mesh for every building.

**Space levels do work**, but not completely. Platforms place, `MapLayers` and
`LiftVerticalOffsetProvider` adapt to any range, and rail lifts span it. What does not adapt
is the authored set of space belt pieces, which cannot reach every new layer — so belt
routing between distant layers may be impossible. It is off by default for that reason; set
`ExtraIslandLayers` to 1 or 2 if the limitation is acceptable to you.

## Translations

Placeholders are **self-closing** tags — `Machine Level <level/>`, bound in code with
`"key".T().Bind("level", new RawText(n))`. A bare `<level>` is an opening tag and fails the
whole mod load with "unclosed xml tags", since translations are parsed when the mod resolves,
before any mod code runs. Styling tags such as `<gl>...</gl>` are the other kind: they are
registered in `TranslationParser.Tags` and expect children.

## Save compatibility

The manifest sets `AffectsSaveGames: false`, deliberately. The game's rule
(`SavegameModdingContextDivergenceBarrier`) is that a mod flagged `true` which is **added to
or removed from** a save's recorded mod list produces a hard "cannot load" error. There is no
"safe to add, unsafe to remove" setting, so `true` would block installing this mod into any
existing world — even though adding it is completely safe: it only appends tiers to upgrades
that already exist and adds new optional shop entries, so nothing in a vanilla save becomes
invalid.

The real hazard is the other direction. Once you have bought a tier past vanilla's maximum,
that level is written to the save, and `ResearchLinearUpgradeManager.GetCurrentLevel` throws
on a level higher than the upgrade has tiers. So:

- **Adding** the mod to an existing save: safe, no dialog.
- **Removing** it after buying extra tiers: breaks that save, with no warning.
- **Lowering** a tier count in `config.json` below a level you already bought: same.

Keep a backup before you first buy past the vanilla cap.

## Building

Needs the three environment variables the sample mods use — `SPZ2_PATH`,
`SPZ2_PERSISTENT`, `SPZ2_SHIFTER`. On Windows, `shapez2.exe --set-modding-env-vars` sets
them.

```
dotnet build                 # installs into <persistent>/mods/ExtendedResearch
dotnet build -p:Dev=true     # stages into <persistent>/mods-dev for Mod Reloader
```

A hot reload only takes effect on the next scenario load, since the scenario is built once.

## Layout

| File | |
|---|---|
| `ExtendedResearchMod.cs` | Entry point; registers the rewirer |
| `ExtendedResearchScenarioExtender.cs` | The hook; discovers which upgrades to extend |
| `LinearUpgradeExtender.cs` | Appends tiers to one linear upgrade |
| `LayerUnlockExtender.cs` | Adds layer mechanics and the shop upgrades that grant them |
| `ExtendedResearchConfig.cs` | Defaults and `config.json` loading |

## License

Apache 2.0 — see `LICENSE` and `NOTICE`.
