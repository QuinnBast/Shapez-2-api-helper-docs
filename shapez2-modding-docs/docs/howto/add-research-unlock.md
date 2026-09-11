# Add a research unlock

**Problem.** New content has to be unlockable, or it never appears on the toolbar.

**Solution.** One of three `Unlocked…` calls in the builder chain. Pick by how much
research UI you want to own.

| Call | Effect | Use when |
| --- | --- | --- |
| `UnlockedAtMilestone(selector)` | unlocks with an existing milestone | simplest — you want it available from some tier onward |
| `UnlockedWithExistingSideUpgrade(selector)` | rides an existing side upgrade | your thing belongs with a vanilla upgrade |
| `UnlockedWithNewSideUpgrade(builder)` | creates a new purchasable upgrade | you want a research node with its own cost and description |

## Unlock at a milestone

```csharp
.UnlockedAtMilestone(new ByIndexMilestoneSelector(^1))   // the last milestone
.UnlockedAtMilestone(new ByIdMilestoneSelector(new ResearchUpgradeId("Milestone_Initial")))
```

`^1` means "the final milestone", i.e. late game. For "available immediately", target
the first milestone by id.

Scenarios use different milestone ids, so a hard-coded id can be wrong in the converter
scenario. Get the real ids from `debug.export-game-data` in the `F1` console rather than
guessing at them.
 Select per scenario instead:

```csharp
.UnlockedAtMilestone(new ByIdPerScenarioMilestoneSelector(MilestoneForScenario))

private ResearchUpgradeId MilestoneForScenario(ScenarioId scenarioId)
{
    return new ResearchUpgradeId(
        scenarioId == /* converter scenario */
            ? "ConverterMilestoneTier_Initial"
            : "Milestone_Initial");
}
```

That is exactly what `BiggerPlatforms` does, and it exists because getting it wrong
means content that never unlocks in one scenario. There is also
`ByIndexPerScenarioMilestoneSelector` when you want positional selection per scenario.

## Create a new side upgrade

```csharp
IPresentableUnlockableSideUpgradeBuilder upgrade = SideUpgrade.New()
   .WithPresentationData(new SideUpgradePresentationData(
        new ResearchUpgradeId("Patience"),   // the node it hangs off
        GameImageId.Empty,
        GameVideoId.Empty,
        "my-mod.cutter.title".T(),
        "my-mod.cutter.description".T(),
        false,
        "Buildings"))                        // research category
   .WithCost(new ResearchCostPoints(new ResearchPointCurrency(50)).AsEnumerable())
   .WithCustomRequirements(Array.Empty<ResearchMechanicId>(), Array.Empty<ResearchUpgradeId>());
```

The chain is enforced in the same way as the building chain — presentation, then cost,
then requirements:

```csharp
IPresentableSideUpgradeBuilder      → WithCost(IEnumerable<IResearchCost>)
ICostingSideUpgradeBuilder          → WithCustomRequirements(mechanics, upgrades)
                                    → CopyingRequirements(sideUpgradeSelector)
                                    → WithoutCustomRequirements()
IPresentableUnlockableSideUpgradeBuilder → WithAdditionalRewards(...)
```

`CopyingRequirements(...)` is the shortcut worth knowing: it clones the prerequisites of
an existing upgrade instead of you enumerating them, which keeps your node consistent
with whatever it sits next to.

Empty requirement arrays mean "no extra prerequisites beyond the parent node" — the
upgrade is purchasable as soon as its parent is reached.

## Costs

```csharp
new ResearchCostPoints(new ResearchPointCurrency(50))
```

`WithCost` takes an `IEnumerable<IResearchCost>`, so multiple costs are possible;
`.AsEnumerable()` wraps a single one. Price it against neighbouring vanilla upgrades in
the same category — a 50-point node next to 5,000-point nodes reads as a bug.

## Gotchas

- **Unlock and toolbar are separate.** Content needs both; a correct toolbar entry with
  no unlock is invisible in-game, which looks exactly like a broken toolbar index.
- The `ResearchUpgradeId` in `SideUpgradePresentationData` is the *parent* node your
  upgrade attaches to. Get it wrong and your node either vanishes or lands in an odd
  part of the tree.
- The category string (`"Buildings"`) must match an existing research category, or the
  node has nowhere to render.
- Milestone **indices** shift between game versions; milestone **ids** are stabler.
  Prefer ids unless you specifically mean "the last one".
- Titles and descriptions are translation keys, not literal text — see
  [Add translations](add-translations.md).
