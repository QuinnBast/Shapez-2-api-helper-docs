# React to the player's selection

**Problem.** You want to show something for the building or platform the player just
clicked.

**Solution.** `Player.InteractionState` exposes live selections plus change events.

## Reading the selection

```csharp
IPlayerInteractionStateManager interaction = GameHelper.Core.LocalPlayer.InteractionState;

foreach (BuildingModel building in interaction.BuildingSelection)
{
    // …
}

foreach (IslandModel island in interaction.IslandSelection)
{
    // …
}
```

Both are `ISelection<T>`, which is an `ICollection<T>` — so `Count`, `Contains`, and
`foreach` all work. Selections are plural because the player can mass-select; the
single-selection case is just `Count == 1`.

## Reacting to changes

```csharp
interaction.BuildingSelection.OnChanged.Register(OnBuildingSelectionChanged);

private void OnBuildingSelectionChanged(IReadOnlyCollection<BuildingModel> selection)
{
    if (selection.Count != 1) { Clear(); return; }

    foreach (BuildingModel building in selection)
    {
        ShowSomethingFor(building);
    }
}
```

`ISelection<T>` also has `OnAdded` and `OnRemoved` if you need the delta rather than the
whole set. `IPlayerInteractionStateManager.OnStateChanged` fires for interaction-mode
changes (placing, selecting, idle) — useful for hiding your UI while the player is
mid-placement, which you can also check with `PlacingAnything`.

Always unregister:

```csharp
interaction.BuildingSelection.OnChanged.Unregister(OnBuildingSelectionChanged);
```

## Adding a panel to the selected building instead

If what you want is a section in the game's own side panel when a building is selected,
do not hand-roll it from the selection — that is what building modules are for.
`BuildingModulesInterceptor` / `IBuildingModulesRewirer` inject modules into the panel,
and the `DiagonalCutter` sample wires them up through Flow. The selection API above is
for *your own* UI or world drawing.

## Gotchas

- Selections hold `BuildingModel` structs. If the player deletes a selected building, a
  copy you stashed is stale — see
  [Find buildings](find-buildings.md#gotchas).
- `InteractionState` lives on `Player`, so it is session-scoped. Register when a map
  loads and unregister when it unloads.
