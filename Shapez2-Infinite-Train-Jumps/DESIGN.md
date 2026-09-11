# Infinite train jumps - design notes

Idea stage. What was read out of the decompiled assemblies rather than assumed, so none of it
has to be re-derived.

## The idea

Let the train launcher and catcher sit arbitrarily far apart, so trains cross the map without
track in between. Originally framed as "train teleportation", but see the first verified fact -
a jump is not a teleport, so the honest pitch is **range**, not instant travel.

## Verified facts

| Question | Answer | Where |
|---|---|---|
| Is a launcher jump instant? | **No.** The train keeps its velocity through the jump. `TickTrainMovement` does `value.Progress += delta * train.Velocity` and only ends the jump once `Progress > value.Length` | `TrainsNavigationSimulator.TickTrainMovement` |
| What sets jump speed? | `AdjustLookaheadSpeed` on the launch coordinator returns `NavigationConfig.TrainLaunchSpeed`, so the lookahead targets that for the whole jump | `TrainLaunchCoordinator.AdjustLookaheadSpeed` |
| How is jump length decided? | `CanLaunch` walks outward `for (int num = MaxLauncherRange; num > MinLauncherRange; num--)` looking for a catcher, and returns `length = num + 1` chunks | `TrainLaunchCoordinator.CanLaunch` |
| So what does doubling the range cost? | Roughly double the travel time, since length is in chunks and traversal is at launch speed | the three rows above |
| Is there a floor on jump duration? | **Yes, one tick per chunk.** The advance is `if (!(train.ChunkProgress < 1f)) AdvanceTrain(...)`, an `if` not a `while`, and `AdvanceTrain` subtracts exactly `1f`. Excess `ChunkProgress` accumulates and never catches up | `TrainsNavigationSimulator.TickTrainMovement`, `AdvanceTrain` |
| Does `SpeedMultiplierPercentage` raise top speed? | **No.** `WithSpeedMultiplier` is applied to `MaxSpeedAhead` and `MaxAcceleration` only; `TickTrain` ends with `train.Velocity = math.clamp(train.Velocity, Config.MinSpeed, Config.MaxSpeed)` against the *raw* max. Above 100% you get harder acceleration to the same top speed | `TrainsNavigationSimulator.TickTrain`, `WithSpeedMultiplier` |
| Where does the config come from? | `TrainConfigurationFactory` builds `TrainNavigationConfiguration` from `MetaTrainSimulationConfiguration`'s public fields, in one call. `ITrainNavigationSimulationConfig` is then injected into the simulator and every coordinator | `TrainConfigurationFactory`, `MetaTrainSimulationConfiguration` |
| Which knobs matter? | `MinLauncherRange`, `MaxLauncherRange`, `TrainLaunchSpeed`, `MaxSpeed`, `MaxAcceleration`, `MaxTurnSpeed`, `MaxDecelerationPerChunk`, `SpeedMultiplierPercentage` - all on the same interface | `ITrainNavigationSimulationConfig` |
| Does raising speed cost CPU? | **Yes, linearly.** `LookaheadTargetVelocity` scans `ceil(Velocity / MaxDecelerationPerChunk)` chunks ahead, and per chunk deep-copies the train into `Allocator.Temp` and polls every navigation coordinator. Raise `MaxDecelerationPerChunk` alongside `MaxSpeed` | `TrainsNavigationSimulator.LookaheadTargetVelocity` |

## What this means for scope

One postfix on `TrainConfigurationFactory` owns train physics globally - no new buildings, no
art, no save divergence, so `AffectsSaveGames: false`.

Two versions, worth deciding between before writing anything:

- **Long jumps** (config only). Raise `MaxLauncherRange`, and raise `TrainLaunchSpeed` and
  `MaxSpeed` together so the longer gap does not read as a slower game. Cheap and safe, but
  the one-tick-per-chunk floor means a 500-chunk jump still takes 500 ticks.
- **Instant jumps** (custom coordinator). To actually skip the traversal, a coordinator would
  have to complete the movement in a single step instead of advancing `ChunkProgress`. That
  means not reusing `TrainLaunchCoordinator`, and checking what the collision map expects -
  buckets are painted and cleaned one wagon-chunk at a time via `CleanLastWagonCollisionBucket`,
  so a skipped traversal must not leave stale buckets.

## Open questions

- What is vanilla `MaxLauncherRange`? It lives in Unity asset data, so it has to be read at
  runtime, not grepped. Same for `MaxSpeed` and `TrainLaunchSpeed`.
- Does the launcher's placement indicator and the catcher-pairing UI respect the config range,
  or is the searched range hardcoded anywhere in the HUD? `TrainLaunchPredictionCoordinator` is
  handed the range explicitly, which is a good sign.
- Rendering: does the train mesh interpolate correctly at high `ChunkProgress`, or does it
  visibly lag once progress saturates?
- `MetaTrainSimulationConfiguration.TrainSpeedResearchId` shows there is already a train-speed
  research track. Extra tiers there may belong in Shapez2-Extended-Research instead of here.
