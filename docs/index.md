# Start Here

- **Got a task?** → [How do I…](howto/index.md) — problem, code, gotcha.
- **Got a type you don't understand?** → [Reference](map-model.md), or search the
  [API](../api/index.md).
- **Got neither yet?** → [Getting Started](getting-started.md) builds a project and
  gets a `.dll` into the game.

## Scope

Setup, `manifest.json`, and dependencies are covered well by the
[official wiki](https://shapez2.wiki.gg/wiki/Mod_Development); ShapezShifter's
content-authoring builders are covered by the
[Notion docs](https://tobspr-games.notion.site/shapez2-modding-documentation).

This site covers what neither does: **working with the game that is already there** —
reading the map, watching machines, drawing over the world, and hooking behaviour that
has no builder API.

## First fifteen minutes

1. [Getting Started](getting-started.md) — build something, watch it load.
2. [Run code when a game loads](howto/run-code-when-game-loads.md) — your constructor
   runs at the main menu, not in a game. This catches everyone.
3. [Find buildings](howto/find-buildings.md) — walk the world.
4. [Add a console command](howto/console-command.md) — the fastest debug loop you get.

## Conventions

Everything stated as fact was read out of the shipped assemblies. Guesses are marked
**Inference, not verified**. Written against shapez 2 `1.0` and the ShapezShifter
workshop release (`steam:3542611357`).

Where a page uses an `[Obsolete]` type — `DrawHooks`, `IGameSessionManagers`,
`IslandModel.LayoutQuery` — it says so. They are the easy way today and the first thing
to break tomorrow.
