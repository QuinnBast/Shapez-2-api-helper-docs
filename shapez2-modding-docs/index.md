---
_layout: landing
---

# shapez 2 Modding Docs

An unofficial, community reference for modding **shapez 2** with the
[ShapezShifter](https://github.com/tobspr-games/shapez2-shifter) API.

This site has two halves:

- **[Guide](docs/index.md)** — hand-written pages that explain how the game is put together:
  the mod lifecycle, the map model, coordinate systems, simulations and item lanes,
  rendering, and how to hook into any of it.
- **[API Reference](api/index.md)** — a generated, searchable index of every public type and
  member in the modding-relevant game assemblies plus `ShapezShifter`.

## Start here

| If you want to… | Read |
| --- | --- |
| Get a project building at all | [Getting Started](docs/getting-started.md) |
| Understand which assembly owns what | [Architecture](docs/architecture.md) |
| Know when your code runs | [Mod Lifecycle](docs/mod-lifecycle.md) |
| Walk islands, buildings, and their state | [The Map Model](docs/map-model.md) |
| Stop guessing what `_G` and `_I` mean | [Coordinate Systems](docs/coordinates.md) |
| Read what a machine is actually doing | [Simulations and Item Lanes](docs/simulations-and-lanes.md) |
| Work out what crosses a platform's boundary | [Ports and Notches](docs/ports-and-notches.md) |
| Draw something in the world | [Rendering](docs/rendering.md) |
| Change behaviour the API doesn't expose | [Hooking the Game](docs/hooking.md) |

## About the API reference

The reference is generated with [DocFX](https://dotnet.github.io/docfx/) directly from
the shipped game assemblies. The game ships **no XML documentation comments**, so the
reference gives you accurate signatures, inheritance, and implementors — but no prose.
That prose is what the Guide is for.

> [!NOTE]
> This is a community effort and is not affiliated with tobspr Games. The official
> documentation lives in
> [Notion](https://tobspr-games.notion.site/shapez2-modding-documentation);
> these pages aim to complement it, not replace it.
