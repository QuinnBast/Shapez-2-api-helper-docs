# Shapez 2 Toolbar Kit

Name-based toolbar placement for Shapez Shifter mods. Shared source, not a mod and not a
shared assembly - there is nothing to install and nothing to declare in `manifest.json`.

## The problem

Shifter locates toolbar elements positionally and only positionally. `IToolbarElementLocator`
exposes `IndexAtLevel` and `Depth`, so `Root().ChildAt(5).ChildAt(4).ChildAt(^1).InsertAfter()`
is the entire vocabulary, and there is no way to create a group at all.

Both failure modes are bad:

- **A stale index inserts successfully, in the wrong place.** Indistinguishable from working.
- **A too-short path throws `ToolbarQueryException` during mod load**, which takes the game's
  whole startup down with it.

Every mod in this repo had the same `Root().ChildAt(5).ChildAt(4)...` copied out of the
SandboxIslands sample, chosen because it resolved rather than because it was right.

## The way in

`IToolbarEntryInsertLocation.AddEntry` is handed the live `ToolbarData`:

```csharp
public interface IToolbarEntryInsertLocation
{
    void AddEntry(ToolbarData toolbarData, IToolbarElementData elementData);
}
```

So an implementation can search the tree, extend it, or both - none of which the index-only
locator can express. That is all the kit is.

Groups are matched on the **translation id** of their title, not the displayed text: every
group carries a `LazyLocalizedText` with a public `TranslationId`, which is stable across game
updates and independent of the player's language.

## Use

```xml
<Import Project="..\..\Shapez2-Toolbar-Kit\ToolbarKit.props"/>
```

```csharp
ToolbarKit.Log = logger;   // once, in the mod constructor. Optional, but do it.

// Into one of the game's own categories or groups.
.InToolbar(ToolbarSlot.InGroup(ToolbarCategory.RegularPlatform))
.InToolbar(ToolbarSlot.InGroup(ToolbarGroup.TrainStations))

// A folder of your own, nested inside one.
.InToolbar(ToolbarSlot.InNewGroup(ToolbarCategory.Rail, "mymod.toolbar.title", icon))

// A new top-level category.
.InToolbar(ToolbarSlot.InNewCategory("mymod.toolbar.title", icon))

// Registered, but not shown.
.InToolbar(ToolbarSlot.Hidden())
```

`Hidden()` exists because Shifter's builder chain makes `.InToolbar(..)` mandatory -
`IDefinedPlaceableIslandExtender` offers no opt-out - so an entity that should exist as a
definition without being individually placeable has nowhere to go. The case that needs it is a
draggable path: the turn pieces have to be registered for the placer's definition finder to
choose from, but nobody picks a corner by hand.

Every method also has a raw-string overload for ids the enums do not cover.

## Two traps the enums exist to remove

- **Every id ends in `.title`.** The category is `island-toolbar.category-Rail`, but the tree
  carries `island-toolbar.category-Rail.title`. Omitting the suffix matches nothing, silently.
- **Casing is not consistent.** `RegularPlatform` and `Rail` are capitalised; `converters` is
  not. That is the game's data.

`ToolbarCategory` covers the seven top-level categories, `ToolbarGroup` the named groups inside
them, and `ToolbarIds.AllCategories` / `.AllGroups` enumerate both. `category.Toolbar()` reports
whether a category is on the building or island toolbar, read off its id prefix - which is the
only thing in the data that says.

## Querying the live tree

The enums describe what shipped. `ToolbarNode` describes what is actually there, including
other mods' entries and anything an update has changed:

```csharp
ToolbarDump dump = new ToolbarDump();
GameRewirers.AddRewirer(dump);
...
foreach (ToolbarNode category in dump.Tree.Categories()) { ... }
dump.Tree.Groups();                       // every folder, any depth
dump.Tree.FindAll(someId);                // every match - ids can repeat
dump.Tree.Find(ToolbarCategory.Rail);     // or by enum
```

Each node carries its `TranslationId`, `Kind`, `Index` (what `ChildAt` uses), `RawIndex` (its
real array position) and `Path`.

All three are idempotent: ten islands asking for one new folder get one folder. So do two
*different* mods asking for the same id - the match is against the live tree, not against
anything either mod remembers, which is why the kit needs no cross-mod coordination despite
each mod carrying its own private copy of it.

## Finding the ids

`ToolbarDump` logs the tree; register it last if you want to see your own entries in it. A
missed lookup prints the same tree automatically, so a wrong id tells you the right ones.

### A Shifter inconsistency worth knowing

`FindElementParent` **skips separators** when walking an index path, but
`ToolbarEntryLocation`'s `Before`/`After` compute the final insert index from
`parent.Children.Count()`, which **counts** them. So the two halves of one path disagree
wherever a separator appears.

This is not academic. `Root().ChildAt(5)` reads like the sixth top-level element, but with a
separator at position 4 it resolves to the *seventh* - Rail, not RegularPlatform. Two mods in
this repo were placing entries one category away from where their comments said, and neither
looked broken. `ToolbarNode` reports both indices for exactly this reason, and naming the
target avoids the question entirely.

## What a miss does

Not an exception, and not a silent misplacement. The entry is appended at the top level - where
it is impossible not to notice - and the full tree is logged with every id in it. The mod still
loads and is still testable.

## Known unknown

`InNewCategory` is implemented but unexercised. Top-level entries appear to be partitioned by
mode through a naming convention (`island-toolbar.category-...`), and how the HUD decides which
toolbar a brand-new top-level category belongs to has not been established. Nesting under a
category known to be on the island toolbar sidesteps the question; that is what the mods in
this repo do.

## Layout

| File | |
|---|---|
| `src/ToolbarSlot.cs` | the three public placements |
| `src/ToolbarTree.cs` | find / create / append by name, and the tree dump |
| `src/ToolbarDump.cs` | `IToolbarDataRewirer` that logs the tree on demand |
| `src/ToolbarKit.cs` | the logger hook |
