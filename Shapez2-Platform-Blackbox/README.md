# Platform Blackbox

Collapse a chunk of factory into a reusable part.

Select some platforms, and the mod tells you what that selection looks like from the
outside — how big it is, and which of its ports actually cross the boundary — then captures
it as a blueprint you can place again.

> **Early days.** What works now is reading a selection, analysing its boundary, and saving
> it as a blueprint. The stand-in platform that *replaces* the selection in place is not
> built yet — see [where this is going](#where-this-is-going).

## Using it

Select platforms in space view, then in the debug console (**F1**):

| | |
|---|---|
| `pbx.analyze` | size of the selection and what crosses its boundary |
| `pbx.ports` | every boundary-crossing port, one per line |
| `pbx.capture <name>` | save the selection to your blueprint library |
| `pbx.export <file>` | write the shareable blueprint string to a file |

`pbx.analyze` on a smelting block might say:

```
7 platforms, 12 chunks, 214 buildings
crossing the boundary: 2 in, 1 out (9 internal ports would be hidden)
smallest platform with room for those ports: 1x1
```

That last line is the useful one. Nine of its eleven ports only talk to other platforms in
the same selection, so they are internal detail — only three connections actually need to
survive, which is few enough to fit on a single 1x1 platform.

## Why the boundary matters

A hundred-building factory with three external connections is a *part*: it takes shapes in
one side and hands finished shapes out the other, and how it does that is nobody's
business. The same hundred buildings with forty external connections is not a part, it is
just a region of your factory, and no 1x1 platform can stand in for it.

So the boundary count is what decides whether a selection can be boxed up at all, which is
why it is the first thing this mod measures.

## Where this is going

The end goal is to replace the selection in place with a small platform that behaves
identically — items in, items out, same rates — with the real factory still running out of
sight, and a way to look inside and edit it.

See [DESIGN.md](DESIGN.md) for the full analysis, including what was measured rather than
assumed and what to try next.

The plan is to **share the recipe rather than the machine**: one copy of the captured
factory's transformation, rate ceiling and port layout, with each placed box carrying only
the item currently passing through it. Fifty boxes then give fifty times the throughput
correctly, while costing one simulation each — so a collapsed factory is genuinely smaller
in the save, not just tidier on the map.

For a straight chain of single-input machines the recipe can be derived **exactly**, by
composing the operations the game already models, rather than approximated from samples.
Selections whose output depends on accumulated state, or on the mix of shapes arriving, do
not reduce to a recipe at all — those the mod should decline to collapse rather than get
quietly wrong.

## Installing

Requires [Shapez Shifter](https://steamcommunity.com/sharedfiles/filedetails/?id=3542611357).
Put the built folder in:

```
%LOCALAPPDATA%Low\tobspr Games\shapez 2\mods\PlatformBlackbox\
```

Nothing is written to your save. In its current form the mod only reads the map and writes
blueprints, so it cannot damage anything.

## Building

Set `SPZ2_PATH`, `SPZ2_PERSISTENT` and `SPZ2_SHIFTER` — running the game once with
`--set-modding-env-vars` does it — then `dotnet build`. Output goes straight to the mods
folder; restart the game to pick up changes.

Built on [Shapez Shifter](https://github.com/tobspr-games/shapez2-shifter) by tobspr Games.
Licensed under [Apache 2.0](LICENSE).
