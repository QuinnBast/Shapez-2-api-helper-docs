# Read production statistics

**Problem.** You want production rates over time — what the factory is delivering — not
the instantaneous state of one machine.

**Solution.** The statistics trackers. The game already records delivery history; read
it rather than sampling it yourself.

## The tracker

```csharp
namespace Game.Core.Statistics;

public interface IGameStatisticsTracker
{
    IReadOnlyList<IStatisticsTracker<UnifiedShapeId>> ShapeDeliveryTrackers { get; }
    IReadOnlyList<IStatisticsTracker<RocketGroupId>> RocketDeliveryTrackers { get; }
}
```

Two things are tracked: **shape deliveries** and **rocket launches**. That is the whole
scope — this is not a general metrics system, and there is no per-building history here.

Both are **lists** of trackers, one per interval (the different time windows the
statistics screen offers), so pick the tracker whose resolution matches your question.

## Buckets

```csharp
public interface IStatisticsTracker<TKey> : IStatisticsStream<TKey>
{
    IReadOnlyList<IStatisticsBucket<TKey>> Buckets { get; }
}
```

Everything is bucketed time series, keyed by what was delivered:

```csharp
foreach (IStatisticsTracker<UnifiedShapeId> tracker in statistics.ShapeDeliveryTrackers)
{
    foreach (IStatisticsBucket<UnifiedShapeId> bucket in tracker.Buckets)
    {
        // per-interval totals per shape
    }
}
```

Note the key is `UnifiedShapeId`, not `ShapeId` — deliveries are aggregated across
shape variants the research system considers equivalent (`ResearchShapeUnifier`). If you
compare against a raw `ShapeId` from a belt you may not get a match.

## The supporting types

Worth knowing they exist before you build anything time-series shaped yourself:

| Type | Role |
| --- | --- |
| `GameStatisticsTracker` | the concrete tracker |
| `IntervalBasedStatisticsTracker` | fixed-interval buckets |
| `ISlidingWindowStatisticsTracker` | a moving window |
| `SlidingWindowStatisticsStreamView` | a view over a window |
| `AggregatedStatisticsTracker` | combines trackers |
| `StatisticsBucket`, `StatisticsStream` | the storage primitives |

If you need a rolling average of something the game does *not* track — machine
efficiency, for instance — this is the vocabulary to imitate rather than reinvent:
bucket by interval, keep a bounded number of buckets, aggregate on read.

## Sending the player to the statistics screen

Cheaper than rendering your own charts:

```csharp
Events?.ShowStatistics.Invoke();
```

See [notifications and HUD screens](notifications-and-hud-screens.md). Pairing a
notification with an action that opens statistics gives you a complete feature without
any UI work.

## The vanilla statistics UI

If you want to add to the existing screen rather than read from it, the tabs are
`HUDStatisticsTabDelivery`, `HUDStatisticsTabStructures`,
`HUDStatisticsTabRocketLaunches`, with `HUDStatisticsChart` doing the drawing. There is
no interceptor for statistics tabs, so extending that screen means detouring
([hooking](../hooking.md)) — which is a lot of work for something a notification and a
side panel section can usually cover.

## Gotchas

- **Delivery only.** There is no built-in per-building production history. Any
  per-machine metric is yours to collect — see
  [read machine state](read-machine-state.md).
- `UnifiedShapeId` ≠ `ShapeId`. Unify before comparing.
- Buckets are bounded; old data rolls off. Do not expect whole-save history.
- Reading every bucket of every tracker every frame is needless — statistics update on
  an interval, so read on the same cadence.

> [!NOTE]
> `IGameStatisticsTracker` is verified, as is the bucket structure. How you obtain the
> instance was not verified — it is not on `IGameSessionManagers`. Expect to reach it
> via the session with the [publicizer](../publicizer.md), or by hooking a type that
> receives it.
