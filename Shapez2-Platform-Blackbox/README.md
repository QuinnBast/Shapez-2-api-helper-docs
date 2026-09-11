# Platform Blackbox

Collapse a chunk of factory into a reusable part.

Select some platforms, and the mod tells you what that selection looks like from the
outside — how big it is, which of its ports cross the boundary, what shapes it turns into
what, and how fast it can do it.

> **Early days.** What works now is measuring: analysing a selection's boundary, reading the
> shapes the game predicts at its ports, and running a private copy of it flat out to find
> its rate. The stand-in platform that *replaces* it is not built yet — see
> [where this is going](#where-this-is-going).

## Using it

Select platforms in space view, then in the debug console (**F1**):

| | |
|---|---|
| `pbx.analyze` | size of the selection, what crosses its boundary, and the platform a box would need |
| `pbx.ports` | the boundary grouped into notches, with each port's slot and layer |
| `pbx.predict` | the shapes the game expects at every boundary port |
| `pbx.sandbox` | copy the selection into a private world and run it |
| `pbx.measure` | saturate that copy and report its ceiling and latency |
| `pbx.recipe` | the same, reached through a blueprint instead of the live map |
| `pbx.capture <name>` | save the selection to your blueprint library |
| `pbx.export <file>` | write the shareable blueprint string to a file |

Every report is also written to `blackbox/` next to your saves, and to the log, because the
in-game console cannot be copied from.

`pbx.analyze` on a painter platform says:

```
1 platform, 1 chunks, 414 buildings
crossing the boundary: 24 in, 12 out (24 internal ports would be hidden)
across 3 notches
smallest platform with room for those notches: 1x1
```

## Why the boundary matters

A four-hundred-building factory with three connections to the outside is a *part*: it takes
shapes in one side and hands finished shapes out the other, and how it does that is nobody's
business.

What decides the size of a box is **notches**, not ports. A notch is one side of one chunk —
four belt positions across, on each of three building layers — so a single notch carries up
to twelve ports. The painter above has 36 ports, but they sit in only three notches, so a
1x1 platform has room for all of them.

That also means a wide boundary never makes a factory un-boxable; it only makes the box a
little longer, because a platform of w by h chunks offers 2(w+h) notches. Whether a factory
can be boxed at all is a separate question, answered further down.

## Measuring what a factory does

`pbx.measure` copies the selection into a world nobody else can see, feeds every input as
fast as it will take items, drains every output so nothing backs up, and counts what comes
out:

```
copied 1 platform, 414 buildings into a private world
the game wired them into 222 simulations

fed 12 item inputs and 12 fluid inputs, drained 12 outputs
  latency  10s before the first item reached an output
  shapes   --CrCrCr

steady output: 2160/min across 12 ports (levelled off)
  per window: 18, 1584, 2160, 2160, 2160, 2160
```

The figure is the **ceiling** — what the factory could do flat out — not what it happens to
be doing. A stand-in has to be able to do what the real thing could do. Measuring keeps going
until the rate stops improving for several windows in a row, because a factory that is still
filling its belts climbs for a long time, and the per-window list is there so you can see
whether stopping was fair.

The latency is not a detail. A box that emits the instant it is fed would behave noticeably
differently from the factory it replaces, every time the factory starts or stalls.

## What the game already knows

`pbx.predict` is the interesting one. Shapez 2 runs a second simulation alongside the real
one whose only job is to work out which shapes can appear where, and it is readable from a
mod. So "what comes out, given what goes in" does not have to be derived — it can be looked
up:

```
crossing the boundary: 24 in, 12 out (24 internal transfers hidden)

in:
  12 x  space   --CuCuCu
  12 x  docked  fluid Red

out:
  12 x  space   --CrCrCr
```

Those boundary figures are worked out twice, by completely separate code — once from the real
simulation graph and once from the prediction graph — and they agree, which is what makes
them worth trusting.

It also knows when it has given up. Once more than four possibilities reach a point the game
marks it *degenerated*, and that is exactly the signal for a factory whose output cannot be
written down as a recipe. **That**, rather than anything about its size or shape, is what
decides whether a factory can be boxed — and the mod declines those rather than getting them
quietly wrong.

## Where this is going

Blueprints are the unit. The end goal is to place a blueprint as a **blackbox** — a single
small platform that behaves identically, items in, items out, same rates — and expand it
back into the real thing whenever you want to look inside.

Building on blueprints rather than on map selections settles a lot for free: a blueprint is
closed by construction, so its ports are a property of the artifact; it already saves,
shares and places; and collapse and expand become exact inverses of operations the game
already has.

The plan is to **share the recipe rather than the machine**: one copy per blueprint of the
transformation, rate ceiling and port layout, with each placed box carrying only the items
currently passing through it. Fifty boxes then give fifty times the throughput correctly,
while costing one simulation each — so a collapsed factory is genuinely smaller in the save,
not just tidier on the map.

Rates are the half the prediction graph does not answer. Those get measured once, offline,
by running the blueprint alone in a private simulation and saturating it — then the
measurement is thrown away and only the recipe is kept.

See [DESIGN.md](DESIGN.md) for the full analysis, including what was measured rather than
assumed and what to try next.

## Installing

Requires [Shapez Shifter](https://steamcommunity.com/sharedfiles/filedetails/?id=3542611357).
Put the built folder in:

```
%LOCALAPPDATA%Low\tobspr Games\shapez 2\mods\PlatformBlackbox\
```

Nothing is written to your save. The measuring commands do run a simulation, but on copies
in a private world with their own state, so they cannot move an item in your factory.

## Building

Set `SPZ2_PATH`, `SPZ2_PERSISTENT` and `SPZ2_SHIFTER` — running the game once with
`--set-modding-env-vars` does it — then `dotnet build`. Output goes straight to the mods
folder; restart the game to pick up changes.

Built on [Shapez Shifter](https://github.com/tobspr-games/shapez2-shifter) by tobspr Games.
Licensed under [Apache 2.0](LICENSE).
