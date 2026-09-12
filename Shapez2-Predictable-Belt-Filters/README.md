# Predictable Belt Filters

Belt filters predict what they actually pass.

![A filter wired to a shape signal: the forward belt predicts only that shape, the side belt
predicts the rest](Screenshots/PredictableBeltFilters.png)

## The problem

shapez 2 runs a second simulation beside the real one whose only job is to work out which
shapes can appear where. It is what fills in the shape readouts on belts, ports and
placement previews.

Belt filters are the one item-routing building whose entry in that graph does not match its
behaviour. The game registers a filter as a plain 1-to-2 splitter, so **both outputs are
predicted to carry everything the input carries** — the forward belt and the side belt show
the same shapes, every time.

That is not just cosmetic. A predicted set holds at most four shapes and silently drops the
fifth, and a machine handed only shapes it cannot process reports "no idea", which the next
belt turns into a blank readout for everything downstream. A filter that never narrows feeds
both problems. The usual report is not "the prediction is a bit off" — it is "my belts
stopped showing anything after I added filters".

## What this mod does

Replaces the filter's prediction with one that follows the filter's real routing rule:

| Filter's wire signal | Forward belt | Side belt |
| --- | --- | --- |
| a shape signal | just that shape | everything else |
| a truthy number | everything | nothing |
| a falsy number | nothing | everything |
| nothing connected | *unchanged from vanilla* | *unchanged from vanilla* |

The last row is deliberate. An unwired filter really does block everything, so predicting
two empty belts would be technically correct — but a filter you just placed and have not
wired yet is the normal case, and blanking both its belts would read as the mod breaking
predictions. Where the filter's rule is not known, the mod answers exactly what the base
game would have answered, so it can add precision but never take information away.

## What it does not fix

- Predictions still hold at most four shapes, and the fifth is still dropped with no marker.
  That limit is baked into the game's data structure.
- Machines that genuinely cannot process what they are handed still blank the readout
  downstream. Correct filtering makes that happen far less often, but the mechanism is
  untouched.

## Install

Requires [Shapez Shifter](https://steamcommunity.com/sharedfiles/filedetails/?id=3542611357).

Safe to add to and remove from an existing save — nothing is serialised. No new buildings,
no toolbar entries, no research.

## Build

Needs `SPZ2_PATH`, `SPZ2_PERSISTENT` and `SPZ2_SHIFTER` set. Close the game first; the
installed dll is memory-mapped while it runs.

```bash
dotnet build                 # installs into <persistent>/mods/PredictableBeltFilters
dotnet build -p:Dev=true     # stages into <persistent>/mods-dev/ for the Mod Reloader
```

`mrl.reload predictablebeltfilters` picks up a staged build, but **reload alone is not
enough for this mod**. It registers systems in both the real and the prediction simulation,
and neither collection is rebuilt by a code reload:

- the prediction systems rebuild when shape predictions are toggled off and on in settings;
- the real systems rebuild only on session load, so exit to the menu and load the save again.

Starting the game normally does both, so a plain `dotnet build` and a restart is the simpler
loop.

## Publish

```bash
dotnet build -t:SteamPublish
```

Uploads whatever is already at the output path to the workshop item named in
`Steam/base.vdf` — it does not build first. See that script's header for the steamcmd login
setup.

## How it works

See [DESIGN.md](DESIGN.md). Short version: the filter's rule comes from a wire signal, and
reading that from the prediction side is harder than it looks — the `BuildingInstance` the
prediction graph hands out carries a placement ghost's empty state container for any newly
placed building. The live container is published from the real simulation side instead and
looked up by tile.

## Licence

Apache 2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE).
