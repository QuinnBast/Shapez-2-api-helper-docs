# Getting Started

## Requirements

- shapez 2 `1.0` or later
- .NET SDK 8 (the mods themselves target `netstandard2.1`)
- [ShapezShifter](https://github.com/tobspr-games/shapez2-shifter) — either the
  [Workshop build](https://steamcommunity.com/sharedfiles/filedetails/?id=3542611357)
  or compiled from source
- Visual Studio, Rider, or VS Code

## The three environment variables

Every sample project resolves its references through three variables. Nothing builds
until they are set:

| Variable | Points at | Example |
| --- | --- | --- |
| `SPZ2_PATH` | the game's managed assemblies | `…\steamapps\common\shapez 2\shapez 2_Data\Managed` |
| `SPZ2_PERSISTENT` | Unity's `Application.persistentDataPath` | `…\AppData\LocalLow\tobspr Games\shapez 2` |
| `SPZ2_SHIFTER` | the `ShapezShifter.dll` file itself | `…\workshop\content\2162800\3542611357\ShapezShifter.dll` |

Note that `SPZ2_SHIFTER` is a **file path**, not a directory, while the other two are
directories.

On Windows the game will set all three for you:

```text
shapez 2.exe --set-modding-env-vars
```

On macOS/Linux, export them from your shell profile and launch your IDE from that
shell so it inherits them.

Verify them before touching any code:

```powershell
Get-ChildItem Env: | Where-Object { $_.Name -like 'SPZ2*' }
```

## Project shape

A mod is a `netstandard2.1` class library whose output path is the game's mod folder,
so a build *is* an install:

```xml
<PropertyGroup>
  <TargetFramework>netstandard2.1</TargetFramework>
  <LangVersion>9</LangVersion>
  <Nullable>disable</Nullable>
  <RootNamespace>MyMod</RootNamespace>
</PropertyGroup>

<PropertyGroup>
  <OutputPath>$(SPZ2_PERSISTENT)\mods\MyMod</OutputPath>
  <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
</PropertyGroup>
```

Game references are added by hand, one `<Reference>` per assembly, always with
`<Private>False</Private>` so the game's own DLLs are not copied into your mod folder:

```xml
<ItemGroup>
  <Reference Include="ShapezShifter">
    <HintPath>$(SPZ2_SHIFTER)</HintPath>
    <Private>False</Private>
  </Reference>
  <Reference Include="SPZGameAssembly">
    <HintPath>$(SPZ2_PATH)\SPZGameAssembly.dll</HintPath>
    <Private>False</Private>
  </Reference>
  <!-- …one per assembly you actually use -->
</ItemGroup>
```

See [Architecture](architecture.md#assembly-map) for which assembly holds what, so you
only reference what you need.

Most mods also want the [publicizer](publicizer.md), which is what makes the game's
`private` and `internal` members visible to your code.

## manifest.json

Sits next to your DLL in the output folder and is what the game reads to list your mod:

```json
{
  "Version": "1.0.0",
  "Description": "What the mod does, in one line",
  "Author": "You",
  "SavedModVersionCompabilityRangeWithSelf": "<1.1.0",
  "GameVersionSupportRange": "*",
  "AffectsSaveGames": true,
  "DisablesAchievements": false,
  "Conflicts": [],
  "Assemblies": [ "MyMod.dll" ],
  "Dependencies": [
    {
      "ModId": "steam:3542611357",
      "ModTitle": "Shapez Shifter",
      "Version": "1.0.*"
    }
  ]
}
```

Points worth knowing:

- `SavedModVersionCompabilityRangeWithSelf` (the typo is in the game) declares which
  versions of *your own* mod can load a save written by this version.
- `AffectsSaveGames: true` means saves touched by the mod are marked as requiring it.
  Set it to `false` only for genuinely cosmetic mods — an overlay that reads state and
  draws, for instance, changes nothing that gets serialized.
- Copy it to the output on every build:

```xml
<None Update="manifest.json">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```

## Your first mod

A mod is any public class implementing `IMod`. The constructor is the entry point, and
its parameters are dependency-injected:

```csharp
using JetBrains.Annotations;
using ILogger = Core.Logging.ILogger;

[UsedImplicitly]
public class MyMod : IMod
{
    private readonly ILogger Logger;

    public MyMod(ILogger logger)
    {
        Logger = logger;
        Logger.Info?.Log("MyMod loaded");
    }

    public void Dispose()
    {
        // undo anything global you installed: hooks, event registrations, meshes
    }
}
```

`[UsedImplicitly]` just silences the IDE — the class is found by reflection, not by any
reference to it.

Build, then launch the game and enable the mod from the mods menu. See
[Mod Lifecycle](mod-lifecycle.md) for what you can safely touch at construction time
(short version: not the map — no save is loaded yet).

## The sample mods

The [official samples](https://github.com/tobspr-games/shapez2-mod-samples) are the
best worked examples that exist:

| Sample | Shows |
| --- | --- |
| **DiagonalCutter** | The full stack: new building + group, custom simulation and simulation system, placer, toolbar entry, HUD side-panel modules, `.fbx` model loading, dynamic rendering, translations, research unlock |
| **SandboxIslands** | Adding an island with simulation — a paint trash island |
| **BiggerPlatforms** | Island/foundation data with no new simulation, i.e. the minimum for a new platform shape |

If you are adding *content* (a building, an island), start by copying the closest
sample. If you are adding *behaviour* over existing content (an overlay, a rebalance, a
UI tweak), the samples will mislead you a bit — you want
[Hooking the Game](hooking.md) instead.
