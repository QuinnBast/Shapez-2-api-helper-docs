# Speed upgrades and buffs

**Problem.** Your building processes at a fixed rate and ignores the player's research
upgrades — so it starts fair and ends up useless once vanilla buildings are upgraded.

**Solution.** Declare a `ResearchSpeedId`. The game multiplies your base duration by the
player's current upgrade level for that id.

## The builtin speed ids

```csharp
public static class BuiltinResearchSpeed
{
    public static ResearchSpeedId CutterSpeed  = new ResearchSpeedId("CutterSpeed");
    public static ResearchSpeedId BeltSpeed    = new ResearchSpeedId("BeltSpeed");
    public static ResearchSpeedId StackerSpeed = new ResearchSpeedId("StackerSpeed");
    public static ResearchSpeedId PainterSpeed = new ResearchSpeedId("PainterSpeed");
    public static ResearchSpeedId TrainSpeed   = new ResearchSpeedId("TrainSpeed");
}
```

Pick the one that matches what your building *is*. A modded cutter variant should scale
with `CutterSpeed` — then it stays balanced against vanilla cutters for free, and the
player's investment applies to it as they expect.

## Using one

Through Flow, at the modules stage:

```csharp
.WithAtomicShapeProcessingModules(BuiltinResearchSpeed.CutterSpeed, 2.0f)
```

The second argument is the **base** processing duration in seconds — the unupgraded
figure. Give the same number to the efficiency data so the panel readout matches:

```csharp
Building.Create(definitionId)
   .WithEfficiencyData(new BuildingEfficiencyData(2.0f, 1));
```

## Computing the effective duration

To know the *actual* current duration — for your own simulation, or to display it:

```csharp
float effective = StructureStatProcessingTime.ComputeEffectiveDuration(
    simulationSpeedsProvider,
    baseDuration,
    speedId);
```

`ISimulationSpeedsProvider` is the source of the multiplier. This is precisely what the
vanilla efficiency panel does before comparing measured throughput against theory
([read machine state](read-machine-state.md#measuring-actual-throughput)) — so if you are
computing efficiency yourself, use this and not your raw base duration, or your
percentage will drift as the player buys upgrades.

## Related stats

```csharp
StructureStatProcessingTime(duration, speedId, outputLanes)
StructureStatBuildingsPerFullBelt(beltSpeed, buildingDuration, buildingSpeed)
StructureStatFluidThroughput
```

These are the rows in the building's info panel. Return them from `GetStats` on your
module provider ([building side panel](building-side-panel.md)) and the player sees the
same stat presentation vanilla buildings get, upgrade-aware.

Which stats appear is partly driven by flags on the definition group —
`ShowStatBuildingsPerFullBelt` and `ShowStatBeltProcessingTime` — as
`ItemSimulationBuildingModuleDataProvider` demonstrates.

## Buffables

The lower-level mechanism, for changing what can be buffed rather than consuming an
existing buff:

```csharp
using ShapezShifter.Hijack;

public class MyBuffablesRewirer : IBuffablesRewirer
{
    public ICollection<object> ModifyBuffables(ICollection<object> buffables)
    {
        // add to, or filter, the set of buffable things
        return buffables;
    }
}
```

Note the signature returns the collection, so this rewirer can replace the set wholesale
rather than only appending. The `object` element type means the contract is loose —
inspect what the game puts in there for your version before assuming a shape.

> [!NOTE]
> No sample uses `IBuffablesRewirer`. Registering a **new** speed id — as opposed to
> reusing a builtin one — also needs the research tree to offer an upgrade for it, which
> is unexplored territory. Prefer a builtin id unless you have a strong reason.

## Gotchas

- **Do not hard-code an upgraded duration.** Give the base value and let the multiplier
  apply, or your building ignores research.
- Keep the duration you give Flow, the duration in your simulation, and the duration in
  your efficiency data **the same number**. Three copies of a constant is three chances
  to disagree; define it once.
- A new `ResearchSpeedId` string that no upgrade targets means a multiplier of whatever
  the default is — effectively an unupgradeable building.
- `TrainSpeed` scales trains, not train-adjacent buildings. Match the id to the thing
  being timed.
