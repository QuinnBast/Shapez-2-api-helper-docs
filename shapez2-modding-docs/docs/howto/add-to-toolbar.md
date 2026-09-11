# Add to the toolbar

**Problem.** Your building or island exists but the player has no way to select it.

**Solution.** `InToolbar(...)`, with a *locator* that describes a position in the
existing toolbar tree by index.

```csharp
using ShapezShifter.Flow.Toolbar;

.InToolbar(ToolbarElementLocator.Root().ChildAt(0).ChildAt(2).ChildAt(^1).InsertAfter())
```

Read that right to left: find the **last** child (`^1`) of the third child (`2`) of the
first child (`0`) of the root, and insert my entry **after** it.

## The API, in full

It is deliberately tiny:

```csharp
// ShapezShifter.Flow.Toolbar
IToolbarElementLocator Root();
IToolbarElementLocator ChildAt(this IToolbarElementLocator locator, Index index);

IToolbarEntryInsertLocation InsertBefore(this IToolbarElementLocator locator);
IToolbarEntryInsertLocation InsertAfter(this IToolbarElementLocator locator);
```

`Index` is the C# index type, so `^1` is "last", `^2` is "second from last", and plain
integers count from the start. Chain `ChildAt` as deep as the tree goes: root →
category → group → entry.

## Finding the indices

There is no by-name lookup, which makes this the fiddliest part of adding content. Two
approaches:

**Count in-game.** Open the toolbar and count categories left to right from zero, then
count within the category. Tedious but immediate.

**Export the game data.** Press `F1` and run `debug.export-game-data`; the JSON it
writes includes the content structure, which is easier to count accurately than the
on-screen toolbar.

**Read the toolbar data.** More reliable, and it survives you being wrong about what
counts as a child. Dump the tree from a
[console command](console-command.md) and read the real structure:

```csharp
IParentToolbarElementData parent = ToolbarElementLocator.Root()
    .ChildAt(0)
    .FindElementParent(toolbarData);
```

`FindElementParent(ToolbarData)` is the extension the locators use internally
(`ToolbarEntryExtensions`), and it is public — walk it and log ids to map the tree once,
then hard-code the indices you found.

### Separators are not counted — but only on half the path

`FindElementParent` filters `ToolbarSlotSeparator` out before indexing, so a separator
occupies a slot in `Children` that `ChildAt` cannot address and does not count.

That makes a dumped tree printing raw array positions disagree with what `ChildAt` means.
With a separator at raw position 4 of the root, `Root().ChildAt(5)` is **not** the sixth
top-level element — it is the seventh. Nothing looks broken when you get this wrong: the
entry appears, just in the wrong category.

Worse, the two halves of one path disagree. `FindElementParent` skips separators walking
down to the parent, but `ToolbarEntryLocation`'s `Before`/`After` compute the final
insert index from `parent.Children.Count()`, which **counts** them:

```csharp
// ToolbarEntryLocation.After
int num = index.IsFromEnd ? (parent.Children.Count() - index.Value) : index.Value;
```

So `ChildAt(^1).InsertAfter()` stays safe — it appends either way — but a numeric leaf
index lands one slot later for every separator ahead of it. Prefer `^1`, and count with
separators excluded for every hop except the last.

## Anchoring so it survives updates

Indices are positional, so a game update that inserts a building ahead of your anchor
moves your entry. Two habits reduce the pain:

- **Anchor at the end of a group** with `ChildAt(^1).InsertAfter()`. Appending after the
  last entry of a category is stable even when entries are added ahead of it, because
  new vanilla entries also land at the end. Both samples do exactly this.
- **Anchor in the category you belong to**, not the one that happens to be adjacent. If
  the player expects your cutter next to cutters, a shifted index at least lands
  somewhere sensible.

Shifter ships no "insert into category by id" API. You can write one, though — see below.

## Insert by name instead

`IToolbarElementLocator` only exposes `IndexAtLevel` and `Depth`, which is why everything
above is positional. But the interface that actually performs the insertion is handed the
entire tree:

```csharp
public interface IToolbarEntryInsertLocation
{
    void AddEntry(ToolbarData toolbarData, IToolbarElementData elementData);
}
```

Implement that yourself and you can search, create, or both — none of which a locator can
express. A by-name insert is about ten lines:

```csharp
sealed class InGroup : IToolbarEntryInsertLocation
{
    private readonly string TitleId;

    public void AddEntry(ToolbarData toolbarData, IToolbarElementData elementData)
    {
        ParentToolbarElementData group = Find(toolbarData.RootToolbarElement, TitleId);
        IParentToolbarElementData target = group ?? (IParentToolbarElementData)toolbarData.RootToolbarElement;
        target.InsertAtIndex(elementData, target.Children.Count());
    }
}
```

**Match on the title's `TranslationId`, not its displayed text.** Every group carries a
`LazyLocalizedText Title` with a public `Id`, which is stable across game updates and
independent of the player's language:

```csharp
string id = (element as ParentToolbarElementData)?.Title?.Id.Id;
```

Two traps in those ids:

- **They end in `.title`.** The category is `island-toolbar.category-Rail`, but the tree
  carries `island-toolbar.category-Rail.title`. Omit the suffix and you match nothing,
  silently.
- **Casing is inconsistent.** `island-toolbar.category-RegularPlatform.title` and
  `island-toolbar.category-Rail.title` are capitalised; `island-toolbar.category-converters.title`
  is not.

The same hook lets you **create** a group. `CategoryToolbarElementData` and
`GroupToolbarElementData` are plain `[Serializable]` classes with public fields, and
`InsertAtIndex` knows how to write `Children` back on both them and `RootToolbarElementData`:

```csharp
CategoryToolbarElementData category = new()
{
    Children = new IToolbarElementData[0],
    Title = new LazyLocalizedText(new TranslationId("my-mod.toolbar.title")),
    Icon = icon
};
```

Leave `MechanicRequiredToUnlock` at its default — `ToolbarBuilder` checks
`!MechanicRequiredToUnlock.IsEmpty` and substitutes `AlwaysCompliantRule` when it is empty,
so the category is simply always visible.

Make find-or-create idempotent: `AddEntry` runs **once per registered entry**, so a mod
adding five islands to one new group calls it five times and must get the same group each
time. Matching against the live tree also means two different mods asking for the same id
share a group without either knowing about the other.

Finally, a no-op `AddEntry` gives you something the builder chain otherwise refuses:
`.InToolbar(...)` is mandatory, so an entity that should exist as a *definition* without
being individually placeable — the turn pieces of a draggable path family, say — has
nowhere to go. A location that declines to insert is the way out.

## Real examples

Building, inserted after the last entry of a cutter group
(`DiagonalCutter`):

```csharp
.InToolbar(ToolbarElementLocator.Root().ChildAt(0).ChildAt(2).ChildAt(^1).InsertAfter())
```

Island, inserted into a sandbox category (`SandboxIslands`):

```csharp
.InToolbar(ToolbarElementLocator.Root().ChildAt(5).ChildAt(4).ChildAt(^1).InsertAfter())
```

## Gotchas

- `InToolbar` is only available at one stage of the builder chain — after placement,
  before simulation. If the method is missing, you are at the wrong stage; see
  [Add a building](add-a-building.md#the-whole-chain).
- Toolbar entries are gated by research. A correctly placed entry that stays hidden
  usually means your unlock has not fired — check
  [Add a research unlock](add-research-unlock.md).
- For deeper surgery than inserting an entry, `ToolbarInterceptor` with
  `IToolbarDataRewirer` / `IToolbarModelRewirer` gives you the whole structure to
  rewrite ([Architecture](../architecture.md#hijack--shapezshifterhijack)).
