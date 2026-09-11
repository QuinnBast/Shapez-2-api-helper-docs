# Add a section to the platform side panel

Selecting a platform opens a side panel, and a mod can add modules to it — the same way
[the building side panel](building-side-panel.md) works, with one difference that will
bite you immediately.

## Providers are registered per island definition, and most islands have none

`IslandsModulesLookup` maps an `IslandDefinitionId` to a single
`IIslandModuleDataProvider`. Vanilla only registers one for islands that have something
particular to say — space belts, space pipes, converters, research stations, train
stations, loaders, miners. **Ordinary building platforms have no provider at all.**

So wrapping the providers that already exist is not enough. That covers converters and
space belts and silently does nothing for the platforms the player spends all their time
on. You need to add providers for definitions that have none:

```csharp
public class MyPanelModules : IIslandModulesRewirer
{
    public void AddModules(IslandsModulesLookup modulesLookup)
    {
        Lookup = modulesLookup;

        // Wrap what vanilla registered, so its modules still show above ours.
        Dictionary<IslandDefinitionId, IIslandModuleDataProvider> providers =
            modulesLookup.IslandModulesMap;

        foreach (IslandDefinitionId definitionId in providers.Keys.ToArray())
        {
            providers[definitionId] = new Provider(providers[definitionId], this);
        }
    }

    /// Called once a map is known, and again as islands appear.
    public void EnsureProvider(IslandDefinitionId definitionId)
    {
        Dictionary<IslandDefinitionId, IIslandModuleDataProvider> providers = Lookup?.IslandModulesMap;
        if (providers == null || providers.ContainsKey(definitionId))
        {
            return;
        }

        providers.Add(definitionId, new Provider(null, this));
    }
}
```

`IslandModulesMap` is private; the publicizer makes it reachable. Note `AddModuleProvider`
uses `Dictionary.Add`, so it throws on a definition that already has a provider — assign
through the dictionary when replacing.

To discover which definitions are actually in play, walk `map.Islands` when a map loads and
subscribe to `IMapModel.OnIslandAdded` for ones placed later. Register the rewirer with
`GameRewirers.AddRewirer`; the Shifter calls `AddModules` from a postfix on
`GameSessionOrchestrator.InjectIslandsModuleProviders`.

## Reuse the efficiency gauge

`HUDSidePanelModuleBuildingEfficiency` is the needle-and-percentage widget you get when
selecting a machine. No vanilla island panel uses it, but you can build one:

```csharp
new HUDSidePanelModuleBuildingEfficiency.Data(
    default(BuildingModel),   // only assigned, never read - a default is safe
    someLocalizedSimulation,  // used for Simulator.GetSimulationTimeFor
    someItemLane,             // the lane it will hook and time
    beltSpeedId,
    baseSecondsPerItem);
```

Three things make this work, none of them obvious:

**The `BuildingModel` is unused.** `InitFromData` assigns it to a private field that
nothing reads, so a platform — which has no building — can pass `default`.

**It measures whatever lane you hand it.** The module installs its own `AcceptHook` on that
lane, timestamps arrivals, and averages the gaps. It never moves items through the lane.

**Its 100% mark is `baseDuration / (speedValue / 100)`.** It re-applies the research
multiplier itself. If you already know the true ceiling, multiply by that same factor so
the two cancel and the gauge agrees with your own numbers:

```csharp
float speedFactor = speeds.GetSpeedValue(beltSpeedId) / 100f;
float baseSecondsPerItem = 60f / myCeilingPerMinute * speedFactor;
```

Get `ISimulationSpeedsProvider` from the session container —
`orchestrator.DependencyContainer.Resolve<ISimulationSpeedsProvider>()` — and the belt
research id from `orchestrator.GetBeltBuildingSpeedId()`.

## Aggregate several lanes into one gauge

Because the module only ever *listens* to the lane, the lane does not have to be real.
Implement `IItemLane` and `IHookableItemReceiver` as a stand-in that reports arrivals from
a whole group, and one gauge can measure a space belt's twelve parallel lanes, or every
output port on a platform, as a single flow:

```csharp
public class AggregateLane : IItemLane, IHookableItemReceiver
{
    public AcceptHookDelegate AcceptHook { get; set; }
    public PreAcceptHookDelegate PreAcceptHook { get; set; }
    public PostAcceptHookDelegate PostAcceptHook { get; set; }
    public IItemReceiver NextLane { get; set; }

    // Nothing is routed through this; it exists to be listened to.
    public Steps MaxStep_S => Steps.Zero;
    public Steps FreeStepsAtTheEnd => Steps.Zero;
    public int ItemCount => 0;
    public bool HasItem => false;
    public IBeltItem GetItem(int index) => null;
    public void Clear() { }
    public bool CanAcceptItem(IBeltItem itemToTransfer) => false;
    public void HandOverItem(IBeltItem item, Ticks remainingTicks) => Report(item, remainingTicks);

    public void Report(IBeltItem item, Ticks remainingTicks)
    {
        AcceptHookDelegate hook = AcceptHook;
        if (hook == null) return;

        IBeltItem forwarded = item;
        Ticks remaining = remainingTicks;
        hook(this, ref forwarded, ref remaining);
    }
}
```

Fire it from wherever you already count items — a `PostAcceptHook` on each real lane, for
instance — and pass the group's combined ceiling as the base duration. Reporting
`Ticks.Zero` for the remaining ticks means "arrived now"; the gauge averages gaps over a
minute, so quantising to the update an item landed in does not move the average.

Without this the gauge reads one lane against the whole group's target — a space belt shows
roughly a twelfth of its real flow.

## Text modules do not update

`HUDSidePanelModuleInfoText` and `HUDSidePanelModuleStats` set their content once in
`InitFromData`. `HUDSidePanelModuleStats` looks live because `HUDSingleStat` has an
`OnUpdate`, but that only animates the tutorial highlight — `GetContent` is called once
when the stat is assigned.

So a number in a text module is a snapshot from when the panel opened. Assigning a
live-computing `IText` does not help either: `HUDLocalizedText.Text` only rebuilds when
the `IText` **object** differs (`if (!object.Equals(value, _Text))`), and the module
assigns it once.

Three stock modules do keep polling, and between them they cover most needs:

| Module | Polls | Renders |
| --- | --- | --- |
| `HUDSidePanelModuleBuildingEfficiency` | its hooked lane | needle + percentage |
| `HUDSidePanelModuleRocketProgress` | two `Func<float>` | fill bar + percentage |
| `HUDSidePanelModuleBeltItemContents` | a `Func<IEnumerable<IBeltItem>>` | item icons in fixed slots |

**`HUDSidePanelModuleRocketProgress` is misnamed.** Its prefab reference is
`HUDSidePanelModulesResources.GenericProgress`; the converter is just its only vanilla
user, for rocket progress. It is the generic current-versus-maximum bar, and the right
choice for a buffer, a queue, or anything with a capacity:

```csharp
yield return new HUDSidePanelModuleRocketProgress.Data(
    icon,
    "my-mod.buffer".T(),
    Color.cyan,
    () => store.Count,        // called every frame
    () => store.Capacity);
```

**No stock module renders an arbitrary live number.** The progress view hardcodes its
value text to `FormatGeneralPercentage`, and its header comes from `InitFromData` and
never changes. So "12 / 25" is not reachable without your own `HUDSidePanelModule` and a
Unity prefab to go with it — the live percentage is what you get. Pick a capacity that
makes the percentage readable if the exact count matters.

```csharp
yield return new HUDSidePanelModuleInfoText.Data(new RawText("18 machines, 3 backed up"));
```

That is fine for counts and states. For a rate, use the gauge.

## Register each definition exactly once

`IslandsModulesLookup.AddModuleProvider` is a plain `Dictionary.Add`, so claiming an id
twice throws:

```
ArgumentException: An item with the same key has already been added. Key: My_Island
  at IslandsModulesLookup.AddModuleProvider
  at ShapezShifter.Flow.Atomic.IslandModulesExtender.AddModules
```

It happens during `InjectIslandsModuleProviders`, which runs while the **main menu** is
being built — so the whole game fails to start, not just your island.

The trap is that `AtomicIslands.Extend()` **always** registers a provider for the
definition it carries. `WithoutModules()` does not mean "no registration"; it registers a
`NoModulesProvider`. So if your island goes through the extender chain, its id is already
claimed by the time your `IIslandModulesRewirer` runs.

Pick one owner per id:

```csharp
// Definitions that travel the chain: give the provider to the chain.
.WithCustomModules(new MyPanelModules(kind))

// Definitions the chain never sees — a mirrored flip partner, say — only those go here.
public void AddModules(IslandsModulesLookup modulesLookup)
{
    modulesLookup.AddModuleProvider(MyIds.Mirrored(kind), new MyPanelModules(kind));
}
```

If you are wrapping providers vanilla already registered (the pattern at the top of this
page), you are *replacing* entries rather than adding them, which is fine — the collision
only arises when two registrations both try to `Add` the same new id.

## See also

- [Measure what a factory is actually moving](measure-throughput.md) — where the numbers come from
- [Add a section to the building side panel](building-side-panel.md) — the per-building equivalent
- [Speed upgrades and buffs](speed-upgrades-and-buffs.md) — `ISimulationSpeedsProvider`
