# Read research progress

**Problem.** You want to gate a feature on how far the player has got — hide an overlay
until a mechanic is unlocked, or show a hint only in the early game.

**Solution.** `ResearchManager`, reachable from the session.

```csharp
ResearchManager research = GameHelper.Core.Research;
```

## Is this unlocked?

The narrow, stable interface — and the one handed to simulation systems:

```csharp
public interface IResearchUnlockManager
{
    bool IsReachable(ILockedViaResearch upgrade);
    bool IsLocked(ILockedViaResearch upgrade);
}
```

```csharp
if (research.UnlockManager.IsLocked(someUpgrade))
{
    return;   // do not offer the feature yet
}
```

Note the two questions are different: **locked** is "the player cannot use this yet",
**reachable** is "the player could get to this from where they are". Content that is
neither locked nor reachable is content the current scenario never offers.

`IResearchUnlockManager` is also a field on `SimulationSystemsDependencies` and
`PredictionSystemsDependencies`, so systems can gate behaviour without reaching for the
session.

## The managers

`ResearchManager` is a façade over a set of narrower managers. This list is the most
useful map of what "research" means in this game:

| Field | Holds |
| --- | --- |
| `Progress` | `ResearchUnlockProgressManager` — what has been unlocked |
| `UnlockManager` | the locked/reachable queries above |
| `LevelManager` | milestone/level progression |
| `PointStorage` | research point currency |
| `ShapeStorage` | shapes delivered to research |
| `ShapeUnifier` | shape equivalence for research goals |
| `CostManager` | what things cost |
| `RewardManager` | what unlocking grants |
| `LinearUpgradeManager` | the incremental upgrades (speeds, limits) |
| `BlueprintCurrencyManager` | blueprint currency |
| `ChunkLimitManager` | the platform chunk budget |

`ChunkLimitManager` is the one people are surprised by: the cap on how much platform the
player may build is research state, so a mod that adds platforms interacts with it.

## Persistence shape

`ResearchManager.SerializedData` tells you exactly what is saved:

```csharp
ResearchProgress   // ResearchUnlockProgressManager.SerializedData
Shapes             // ResearchShapeStorage.SerializedData
BlueprintCurrency  // BlueprintCurrencyManager.SerializedData
PointCurrency      // ResearchPointStorage.SerializedData
LinearUpgrades     // ResearchLinearUpgradeManager.SerializedData
PlayerLevel        // ResearchPlayerLevelManager.SerializedData
PlayerLevelGoals   // ResearchPlayerLevelGoalManager.SerializedData
```

Useful for two reasons: it is the definitive list of what research state exists, and it
tells you what you would have to migrate if you ever changed it.

## Reacting to changes

Prefer an event over polling. The research managers expose change events in the same
style as the rest of the codebase (`IEvent` / `MultiRegisterEvent`) — register on the
manager whose state you care about, and unregister when the map unloads
([run code when a game loads](run-code-when-game-loads.md)).

Polling `IsLocked` every tick is wasteful; the answer changes a handful of times per
session.

## Gotchas

- **Read, do not write.** Granting unlocks or points from a mod desynchronises the
  research UI and makes saves hard to reason about. If your mod is meant to grant
  progression, do it through the research reward system when adding content
  ([add a research unlock](add-research-unlock.md)), not by poking storage.
- Research state is per save. Nothing here is a global preference.
- Scenarios differ. A mechanic that exists in the standard scenario may be absent in
  another, so `IsReachable` is the safer gate for "will this player ever have it".
- `GameHelper.Core` is `[Obsolete]`. If you are inside a simulation system, use the
  `IResearchUnlockManager` you were handed instead.

> [!NOTE]
> The manager list is read off `ResearchManager`'s fields; the specific event names on
> each sub-manager were not individually verified. Check <xref:Root.ResearchManager> in the API reference for the
> manager you need.
