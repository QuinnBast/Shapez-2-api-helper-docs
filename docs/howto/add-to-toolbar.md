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

## Anchoring so it survives updates

Indices are positional, so a game update that inserts a building ahead of your anchor
moves your entry. Two habits reduce the pain:

- **Anchor at the end of a group** with `ChildAt(^1).InsertAfter()`. Appending after the
  last entry of a category is stable even when entries are added ahead of it, because
  new vanilla entries also land at the end. Both samples do exactly this.
- **Anchor in the category you belong to**, not the one that happens to be adjacent. If
  the player expects your cutter next to cutters, a shifted index at least lands
  somewhere sensible.

There is no "insert into category by id" API. If your entry lands in the wrong place
after an update, the fix is re-counting indices — one line, but you have to notice.

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
