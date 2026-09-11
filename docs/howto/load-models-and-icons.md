# Load models and icons

**Problem.** Your building needs a mesh, an icon, and possibly a Unity asset bundle —
all loaded from your own mod folder at runtime.

**Solution.** `ShapezShifter.Kit` has a loader for each, and `ModDirectoryLocator`
finds your files without you hard-coding paths.

## Find your own files

```csharp
using ShapezShifter.Kit;

ModFolderLocator resources = ModDirectoryLocator.CreateLocator<MyMod>().SubLocator("Resources");

string iconPath = resources.SubPath("MyCutter_Icon.png");
string meshPath = resources.SubPath("MyCutter.fbx");
```

`CreateLocator<T>()` resolves the folder of the assembly that `T` lives in, so it works
wherever the player's mod folder is. Never build paths from `SPZ2_PERSISTENT` at runtime
— that is a build-time variable.

> [!WARNING]
> **This throws under a hot-reloader.** `CreateLocator<T>()` is
> `Directory.GetParent(typeof(T).Assembly.Location)`, and a reloader has to load with
> `Assembly.Load(byte[])` — `LoadFrom` binds by assembly identity and would just hand back
> the copy already loaded, which is precisely why the byte-array overload is needed. An
> assembly with no file behind it reports `Location == ""`, so `GetParent("")` throws
> `ArgumentException: Path cannot be the empty string`. The throw lands in your mod's
> constructor, so the reload reports the mod as dead with no obvious cause.
>
> This affects **every mod that loads an icon off disk**, which is every mod with a
> toolbar entry. Guard it:
>
> ```csharp
> string location = typeof(MyMod).Assembly.Location;
> ModFolderLocator resources = !string.IsNullOrEmpty(location)
>     ? ModDirectoryLocator.CreateLocator<MyMod>().SubLocator("Resources")
>     : new ModFolderLocator(Path.Combine(
>         Application.persistentDataPath, "mods-dev", "MyMod")).SubLocator("Resources");
> ```
>
> `Application.persistentDataPath` is the same folder `SPZ2_PERSISTENT` points at, and is
> safe to read at runtime — unlike the build-time variable. Prefer `mods-dev` over `mods`
> in the fallback, since that is where a reloader stages from.

Copy resources to the output:

```xml
<None Update="Resources/*">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```

## Icons

```csharp
Sprite icon = FileTextureLoader.LoadTextureAsSprite(iconPath, out _);

BuildingGroup.Create(groupId).WithIcon(icon);
```

The `out` parameter hands back the underlying texture, which the samples discard with
`out _`. Keep it if you need to dispose or reuse the texture yourself.

## Meshes

```csharp
Mesh baseMesh = FileMeshLoader.LoadSingleMeshFromFile(meshPath);
LOD6Mesh lod = MeshLod.Create().AddLod0Mesh(baseMesh).BuildLod6Mesh();
```

The game renders at six levels of detail. `AddLod0Mesh(mesh).BuildLod6Mesh()` supplies
one mesh and lets it stand in for every level — fine for a small building, wasteful for
anything large or numerous. `ILodMeshBuilder` has `AddLod1Mesh` … `AddLod5Mesh` for
supplying progressively simpler meshes.

`.fbx` loading goes through AssimpNet, which ships with ShapezShifter.

## Wiring a mesh into draw data

```csharp
private static BuildingDrawData CreateDrawData(ModFolderLocator resources)
{
    Mesh baseMesh = FileMeshLoader.LoadSingleMeshFromFile(resources.SubPath("MyCutter.fbx"));
    LOD6Mesh lod = MeshLod.Create().AddLod0Mesh(baseMesh).BuildLod6Mesh();

    return new BuildingDrawData(
        renderVoidBelow: false,
        new ILODMesh[] { lod, lod, lod },     // per-variant meshes
        lod,                                   // …and the remaining slots
        lod,
        lod.LODClose,
        new LODEmptyMesh(),
        BoundingBoxHelper.CreateBasicCollider(baseMesh),
        new MyDrawData(),
        false,
        null,
        false);
}
```

`BuildingDrawData` takes a long positional argument list with no named-parameter help
beyond the first — copy the shape from `DiagonalCutterMod.CreateDrawData` and substitute
your meshes rather than working out each slot from scratch.

`BoundingBoxHelper.CreateBasicCollider(mesh)` derives a collider from the mesh, which is
what you want unless the visual mesh is a poor proxy for the footprint.

## Asset bundles

For content authored in Unity (prefabs, materials, shaders) rather than a bare mesh:

```csharp
using var assetBundleHelper =
    AssetBundleHelper.CreateForAssetBundleEmbeddedWithMod<MyMod>("Resources/MyBundle");
```

Note the `using` — it is disposable, and the samples scope it to the constructor where
the content is registered. The path is relative to your mod folder and omits the
extension; a `.manifest` file sits alongside the bundle.

`MaterialHelper` and `ShaderHelper` are the companions for pulling materials and shaders
out of a bundle or off the game's own theme.

## Gotchas

- **Load in the constructor, not per frame.** Mesh and texture loading is expensive and
  the results are meant to live for the process.
- Missing files throw at mod load, which is the good failure — a mod that fails loudly
  at startup beats one that renders nothing. Do not swallow these exceptions.
- Asset bundles are Unity-version-sensitive. A bundle built against a different Unity
  version than the game ships will fail to load, and the error will not be obvious.
- `Resources/*` in the csproj is not recursive. Sub-folders need their own entry.
