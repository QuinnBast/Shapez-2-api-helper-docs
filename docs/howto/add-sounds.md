# Add sounds

**Problem.** Your building is silent while every vanilla machine clunks and hums.

**Solution.** Attach a `BuildingSoundDefinition` and let the sound manager drive it from
lane activity. You do not play sounds yourself.

## Opting out first

Both the samples do this:

```csharp
Building.Create(definitionId)
   .WithoutSound()
```

`WithoutSound()` is a deliberate choice, not a placeholder — a silent building is
perfectly acceptable, and it is the right call while you are still getting the machine
working.

## The definition

```csharp
public class BuildingSoundDefinition : IBuildingSoundDefinition
{
    public SoundLOD SoundLOD { get; }
    public SoundPriority SoundEffectPriority { get; }
    public IBuildingCustomSoundData CustomSoundData { get; }

    public BuildingSoundDefinition(
        SoundLOD soundLOD,
        SoundPriority soundEffectPriority,
        IBuildingCustomSoundData customSoundData);
}
```

Three concerns, and the first two matter more than they look:

- **`SoundLOD`** — at what distance/zoom the sound is audible at all. shapez 2 factories
  contain thousands of machines; without LOD every sound would play at once.
- **`SoundPriority`** — which sounds win when the mixer is saturated. Set this modestly.
  A modded building that outranks vanilla machines will be the loudest thing in the
  player's factory and they will uninstall it.
- **`CustomSoundData`** — your actual clips.

It is attached as custom data on the building definition, the same way draw data and
efficiency data are — `BuildingDefinitionFactory` does
`buildingDefinition.CustomData.Attach(new BuildingSoundDefinition(...))`
([custom data](../map-model.md#custom-data-on-a-definition)).

## How sounds get triggered

```csharp
public interface IBuildingSoundManager
{
    void RegisterSounds(BuildingModel building, IBeltLaneSoundDefinition soundDefinition,
                        SingleItemLane lane, LaneSound[] laneSFxs);

    void UpdateSounds(BuildingModel building, IBeltLaneSoundDefinition soundDefinition,
                      SingleItemLane lane, LaneSound[] laneSounds);

    void TriggerSounds(BuildingModel entity, IBeltLaneSoundDefinition soundDefinition,
                       SingleItemLane lane, LaneSound[] laneSFxs, Ticks progress_T);
}
```

Note every method takes a `SingleItemLane`: **sounds are driven by lanes**, not by your
simulation logic. You declare which sounds correspond to which lane events, and the
manager decides when to actually play them based on LOD, priority, and how many
identical sounds are already going.

`IBuildingSoundManager` is injected into building renderers — `DiagonalCutterSimulationRenderer`
takes one in its constructor (and ignores it, since the cutter is silent). That is where
you would use it for a building with sound.

## Custom sound assets

Clips come from a Unity asset bundle, not a loose `.wav`
([load models and icons](load-models-and-icons.md#asset-bundles)):

```csharp
using var bundle = AssetBundleHelper.CreateForAssetBundleEmbeddedWithMod<MyMod>("Resources/MySounds");
```

## Gotchas

- **Do not call Unity audio directly.** Playing an `AudioSource` per building bypasses
  LOD, priority, and the player's volume settings, and will sound terrible at scale.
- Match vanilla loudness. Author your clips against a recorded vanilla machine rather
  than in isolation.
- Sound is per-lane-event, so a machine with three lanes can make three noises. That is
  usually two too many.
- `WithoutSound()` is not a bug to be fixed later. Ship silent if you have no good
  clips; silence is better than an annoying loop the player cannot disable.

> [!NOTE]
> No sample mod ships sound — both call `WithoutSound()`. This page is derived from the
> interfaces and from how the game attaches its own sound definitions, so treat the
> asset-side details as unverified and expect to experiment.
