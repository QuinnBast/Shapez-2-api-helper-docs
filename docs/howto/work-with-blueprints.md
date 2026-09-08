# Work with blueprints

**Problem.** You want to read the player's blueprint library, add a blueprint to it, or
understand the `.spz2bp` format.

Blueprints get five assemblies to themselves, which tells you how much is going on:
`Game.Core.Blueprint`, `.Serialization`, `.Importer`, `.Exporter`, and
`Game.Blueprints`.

## The library

```csharp
public class BlueprintLibrary : IBlueprintLibrary, IDisposable
{
    public const string FilenameSuffix = ".spz2bp";

    public BlueprintLibraryFolder RootEntry { get; }
    public IReadOnlyList<IBlueprintLibraryEntry> Shortcuts { get; }

    public IEvent OnFullyRefreshed { get; }
    public IEvent OnShortcutsChanged { get; }

    public void Refresh();
    public bool TryRenameEntry(IBlueprintLibraryEntry entry, string newTitle);
    public bool TrySaveChangedBlueprint(BlueprintLibraryEntry entry);
    public bool TryCreateFolder(string name, BlueprintLibraryFolder parent);
    public bool TryBindShortcut(int index, IBlueprintLibraryEntry entry);
}
```

Key facts:

- Blueprints are **files on disk** — `.spz2bp` under
  `%USERPROFILE%\AppData\LocalLow\tobspr Games\shapez 2\blueprints\`. The library is a
  view over a folder tree, not save data.
- `RootEntry` is a `BlueprintLibraryFolder`, so the structure is recursive — walk it to
  enumerate everything.
- Every mutator is a `Try…` returning `bool`. Blueprint operations touch the filesystem
  and fail for ordinary reasons; check the result.
- `Refresh()` re-reads from disk. Call it if you have written files yourself, not on a
  timer.

## Adding a blueprint via the HUD

The path that needs no serialisation knowledge:

```csharp
Events?.RequestAddBlueprintToLibrary.Invoke(annotatedBlueprint, slotIndex);
```

The HUD handles writing and refreshing. See
[notifications and HUD screens](notifications-and-hud-screens.md) for getting
`HUDEvents`.

```csharp
Events?.ShowBlueprintLibrary.Invoke();   // just open the library
```

## Blueprint currency

Placing blueprints costs currency, which is research state, not blueprint state:

```csharp
ResearchManager research = GameHelper.Core.Research;
BlueprintCurrencyManager currency = research.BlueprintCurrencyManager;
```

`BlueprintCurrencyShape` and `BlueprintCostPlacementProcessor` are the related pieces —
cost is computed at placement time from what the blueprint contains, so a mod that adds
buildings implicitly affects blueprint costs.

## The processing pipeline

Placing a blueprint runs it through processors and caches, which is where a mod's new
building has to behave:

| Type | Role |
| --- | --- |
| `IBlueprintProcessor` | transforms a blueprint during placement |
| `BlueprintCachedDataProcessor` | builds cached placement data |
| `ICachedBuildingBlueprintData`, `ICachedIslandBlueprintData` | per-entity cached data |
| `BlueprintBuildingStaticAllowabilityCacheComputer` | whether a building may be placed there |
| `BlueprintCostPlacementProcessor` | applies cost |
| `IBlueprintMigrators` | upgrades old blueprint files to the current format |
| `IBlueprintPlacementToIndexMapper` | maps placements to indices |
| `BlueprintIcon` (+ `…ComponentShape`, `…ComponentIcon`) | the composed icon |

`IBlueprintMigrators` is the interesting one: the format is versioned and migrated
(`BlueprintLegacyIconDeserializer` exists for exactly this), so blueprints outlive
format changes.

## What this means for a mod that adds buildings

You mostly get blueprint support for free — a building added through Flow is
serialisable by definition id and will round-trip. Two caveats:

- **Definition ids are the serialised identity.** Renaming your building's id breaks
  every blueprint containing it, on top of breaking saves.
- **A blueprint containing your building is unplaceable without your mod.** That is
  expected behaviour, but it is worth saying in your mod description if players will
  share blueprints.

## Gotchas

- Blueprints live in the persistent data folder and are shared across saves. Do not
  treat them as per-save state.
- Do not write `.spz2bp` files by hand. Go through the importer/exporter or the HUD
  event; the format is versioned and migrated.
- `.spz2bp` files are also shared as strings between players. Anything you add to a
  blueprint travels to people who may not have your mod.

> [!NOTE]
> `BlueprintLibrary`'s public surface is verified. How a mod obtains the instance, and
> how to construct an `IAnnotatedBlueprint` from scratch, are not — no sample does
> either. Prefer the HUD event over building blueprints programmatically.
