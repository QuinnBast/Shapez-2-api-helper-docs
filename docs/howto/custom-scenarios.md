# Custom scenarios and game modes

**Problem.** You want to change the rules — starting content, progression, limits — not
add a building.

There are **two** routes, and they are for different things.

## Route 1: JSON, no code

The game exports its own content and reads modified copies back:

```text
F1
debug.export-game-data
```

Edit the exported JSON, then place your scenario in the `custom-scenarios` folder:

```text
%USERPROFILE%\AppData\LocalLow\tobspr Games\shapez 2\custom-scenarios\
```

There is also `custom-scenario-parameter-presets\` alongside it for parameter presets.

This is the documented, supported path, and the
[wiki's Custom Game Modes page](https://shapez2.wiki.gg/wiki/Custom_Game_Modes) covers
it properly — start there. No compilation, no API, and it survives game updates far
better than code does.

**Use JSON when** you are changing values, starting conditions, progression order, or
available content.

## Route 2: code, via the scenario interceptor

```csharp
using ShapezShifter.Hijack;

public class MyScenarioRewirer : IGameScenarioRewirer
{
    public GameScenario ModifyGameScenario(GameScenario gameScenario)
    {
        // inspect and return a modified scenario
        return gameScenario;
    }
}

RewirerHandle handle = GameRewirers.AddRewirer(new MyScenarioRewirer());
```

Note the signature **returns** a `GameScenario` — so this rewirer can transform or wholly
replace the scenario the game is about to use, not merely append to it.

**Use code when** the rule you want cannot be expressed as data: behaviour that depends
on runtime state, or a mechanic that needs new logic.

This is also the hook Flow uses under the hood — `GameScenarioBuildingExtender` and
`GameScenarioIslandExtender` are how `AtomicBuildings.Extend().AllScenarios()` gets your
building into scenario data. So if all you want is to add content to every scenario, use
Flow and let it do this for you.

## Scenario ids and per-scenario behaviour

Scenarios are identified by `ScenarioId`, and content can target them selectively.
`.AllScenarios()` is the blanket option; the per-scenario selectors exist because
scenarios genuinely differ:

```csharp
.UnlockedAtMilestone(new ByIdPerScenarioMilestoneSelector(scenarioId =>
    new ResearchUpgradeId(scenarioId == converterScenario
        ? "ConverterMilestoneTier_Initial"
        : "Milestone_Initial")))
```

Milestone ids are **not** shared between scenarios. This is the single most common
scenario-related bug in mods — see
[add a research unlock](add-research-unlock.md#unlock-at-a-milestone).

## Which route to pick

| Want | Route |
| --- | --- |
| Different starting shapes, costs, limits | JSON |
| A different progression order | JSON |
| Content added to all scenarios | Flow (`.AllScenarios()`) |
| A rule that depends on runtime state | `IGameScenarioRewirer` |
| To replace a scenario entirely | `IGameScenarioRewirer` |

Prefer JSON. It needs no build step, no API compatibility, and players can inspect and
share it.

## Gotchas

- **A code-modified scenario affects saves.** Set `AffectsSaveGames: true` in
  `manifest.json` and think about what happens to a save if the player disables your
  mod.
- Exported game data is a snapshot of *your* game version. Re-export after an update
  rather than carrying an old file forward.
- Scenario changes and content additions interact: content added via Flow lands in
  scenario data, so a rewirer that replaces the scenario can drop other mods' content.
  Transform, do not replace, if you want to coexist.
- Custom scenarios live in the **persistent data folder**, not the mod folder — they are
  not shipped inside your DLL.

> [!NOTE]
> The `GameScenario` type is large and no sample mod modifies it directly. This page
> covers the entry point honestly; the shape of what you can change inside a
> `GameScenario` is unexplored — inspect <xref:Root.GameScenario> in the API
> reference before committing to this route.
