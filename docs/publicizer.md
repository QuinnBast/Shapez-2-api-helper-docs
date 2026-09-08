# The Publicizer

Much of what a mod needs is `private` or `internal` — `GameSessionOrchestrator.Draw`,
`MapDrawer.SubDrawers`, half the fields on the HUD parts. The sample projects solve this
with [Krafs.Publicizer](https://github.com/krafs/Publicizer), which rewrites the
*reference* assemblies at build time so every member appears public to your compiler.

## Setup

```xml
<PropertyGroup>
  <PublicizeAll>true</PublicizeAll>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Krafs.Publicizer" Version="2.3.0">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
</ItemGroup>
```

`PublicizeAll` publicizes every referenced assembly. You can instead opt in per
assembly with `<Publicize Include="SPZGameAssembly" />`.

Useful extras the samples set:

```xml
<PropertyGroup>
  <PublicizerClearCacheOnClean>true</PublicizerClearCacheOnClean>
  <PublicizerLogFilePath>logs/krafs.log</PublicizerLogFilePath>
</PropertyGroup>
```

The log is worth keeping — it is how you confirm publicization actually ran.

## What it does and does not do

It changes **compile-time visibility only**. At runtime the members are still private,
and the CLR would normally refuse the access. That works because the publicizer also
emits `IgnoresAccessChecksToAttribute` for the target assemblies, which tells the
runtime to skip the check for your assembly.

Two consequences:

- Access to a private member is a normal, fast field or method access — no reflection
  cost.
- If publicization silently fails, you get a `MethodAccessException` or
  `FieldAccessException` at runtime rather than a compile error.

## `DoNotPublicize`

The sample `.csproj` files exclude a handful of types:

```xml
<ItemGroup>
  <DoNotPublicize Include="SPZGameAssembly:BeltLaneRenderingDefinition"
                  IncludeCompilerGeneratedMembers="false" IncludeVirtualMembers="false"/>
  <DoNotPublicize Include="Game.Core.Coordinates"
                  IncludeCompilerGeneratedMembers="false" IncludeVirtualMembers="false"/>
  <DoNotPublicize Include="SPZGameAssembly:BuildingDefinition" … />
  <DoNotPublicize Include="Game.Orchestration:ParentToolbarElementData" … />
  <DoNotPublicize Include="Game.Orchestration:RootToolbarElementData" … />
</ItemGroup>
```

The reason is that publicizing everything can *break* code that was compiling fine.
Making a private virtual member public changes overload resolution and override
requirements; making the whole of `Game.Core.Coordinates` public exposes internal
constructors that then compete with the intended factory methods. When you hit a
mysterious compile error in code you did not change, adding the offending type to
`DoNotPublicize` is usually the fix.

The syntax is `Assembly` for a whole assembly or `Assembly:TypeName` for one type.

## When it bites

- **A build warning that nothing was publicized** — `"Assembly is marked for
  publicization, but no members were publicized"` — means your references are not
  resolving. Check `SPZ2_PATH`.
- **`Private` matters.** Game references must be `<Private>False</Private>`; copying a
  publicized game assembly into your mod folder would shadow the real one.
- **Private members are not API.** Anything you reach this way can be renamed or deleted
  by an update without notice. The same discipline as
  [hooking](hooking.md#staying-compatible) applies: use them, but keep the uses few and
  in one place.
