# Add translations

**Problem.** Your building's name shows up as `my-mod.cutter.title` in-game.

**Solution.** Everything player-facing is a translation key, not a string. Ship a
`translations.json` next to your DLL and resolve keys with `.T()`.

## The file

```json
{
  "en-US":
  {
    "building-variant.cutter-diagonal.title": "Diagonal Destroyer",
    "building-variant.cutter-diagonal.description": "<gl>Destroys</gl> the <gl>Even Parts</gl> of a shape."
  },

  "pt-BR":
  {
    "building-variant.cutter-diagonal.title": "Eliminadora de diagonais"
  }
}
```

Top level is language code, then flat key → text. Languages may be partial — the
`pt-BR` block above translates only the title, and the description falls back to
`en-US`. Always provide a complete `en-US` block as the fallback.

Copy it to the output on every build:

```xml
<None Update="translations.json">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```

## Using a key

```csharp
using Core.Localization;

IText title = "my-mod.cutter.title".T();
```

`.T()` turns a key into an `IText`, which is what every builder wants:

```csharp
BuildingGroup.Create(groupId)
   .WithTitle("my-mod.cutter.title".T())
   .WithDescription("my-mod.cutter.description".T())
```

`IText` is resolved lazily at display time, so it follows the player's language setting
and updates if they change it. Never pass a literal string where an `IText` is
expected — you would hard-code English.

## Language codes

The codes the game recognises, from `BuiltinLanguagesExtensions.Code()`:

| Language | Code | Language | Code |
| --- | --- | --- | --- |
| English | `en-US` | Polish | `pl` |
| German | `de-DE` | Brazilian Portuguese | `pt-BR` |
| Spanish | `es-ES` | Russian | `ru` |
| French | `fr-FR` | Thai | `th` |
| Japanese | `ja` | Turkish | `tr` |
| Korean | `ko` | Traditional Chinese | `zh-Hant` |
| | | Simplified Chinese | `zh-Hans` |

Note the inconsistency — some are language-region (`en-US`, `pt-BR`), some are bare
(`ja`, `ru`). Use exactly what the table says.

## Naming keys

Vanilla uses dotted, kebab-cased paths:
`building-variant.cutter-diagonal.title`. Two workable conventions for a mod:

- **Mimic vanilla** (`building-variant.my-cutter.title`) — fits in, but risks colliding
  with a future vanilla key.
- **Namespace with your mod** (`my-mod.cutter.title`) — collision-free, and obvious in a
  translator's diff which keys are yours. Preferred.

Some keys are **not** yours to name. Keybinding titles are looked up as
`keybinding.<id>` and `keybinding.<id>.description` from the binding's own id, so the
key follows whatever id you registered.

## Markup

Descriptions accept the game's inline tags — `<gl>…</gl>` in the sample highlights a
term. Copy the tags vanilla uses for the same kind of emphasis rather than inventing
formatting.

## Gotchas

- A missing key renders as the raw key. That is your signal the file did not copy, the
  language block is missing, or the key is misspelled — check the output folder first.
- Keys are flat strings, not nested objects. Dots are part of the name, not structure.
- `translations.json` is loaded by the game's mod loader, not by ShapezShifter, so the
  filename and location are fixed by convention: next to your DLL.
