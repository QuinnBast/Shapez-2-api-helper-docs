# Add a toggle to the visualization bar

The buttons in the bottom-right — island grid, super chunk coordinates, shape resources —
are `HUDVisualization` subclasses. A mod can add one, which is a much better home for an
overlay than a hotkey: the button is discoverable, the on/off state persists between
sessions, and the fade animation comes for free.

## Subclass `HUDVisualization`

```csharp
public class MyVisualization : HUDVisualization
{
    /// The HUD builds visualizations through the game's own dependency factory, which
    /// knows nothing about your mod, so hand your state over through a static.
    public static MyOverlay Target;

    private readonly MyOverlay Overlay;

    public override bool IsAvailable => true;

    public MyVisualization(Player player, Viewport viewport)
        : base(player, viewport, "my-overlay")
    {
        Overlay = Target;
    }

    public override Sprite GetIcon()
    {
        return Globals.Resources.Icons.StatSpeed;
    }

    public override IText GetTitle()
    {
        return new RawText("My overlay");
    }

    public override void OnGameUpdate(InputDownstreamContext context, FrameDrawOptions options)
    {
        base.OnGameUpdate(context, options);

        // Alpha is tweened by the base class as the button is toggled.
        Overlay.Alpha = Alpha;
    }
}
```

What the base class does for you, given only that:

- **Persists the toggle.** The constructor's `id` becomes the preferences key
  `visualization.<id>.enabled`, read on construction and written by `SetEnabled`. Pass
  `defaultActive: true` to the base constructor to start switched on.
- **Drives the button.** `HUDVisualizations.OnGameUpdate` calls `SetActive(IsUserEnabled)`
  each frame, hides the button when `IsAvailable` is false, and shows it as pressed when
  enabled.
- **Fades.** `SetActive` tweens the protected `Alpha` field from 0 to 1. Multiply your
  overlay's opacity by it and switching on and off eases rather than snapping.
- **Tooltip.** `GetTitle()` becomes the hover tooltip.

Override `IsForcedActive` to run regardless of the user's choice — that hides the button,
which is how the super chunk coordinate labels behave in island placement mode.

## Get it into the list

`HUDVisualizations` builds its list in a private `[Construct]` method, using
`AddVisualization<T>()`. The publicizer makes that callable, so add yours from a postfix
on the part's update, guarding so it only happens once per HUD:

```csharp
VisualizationsHook = DetourHelper.CreatePostfixHook<HUDVisualizations, InputDownstreamContext, FrameDrawOptions>(
    (visualizations, context, options) => visualizations.OnGameUpdate(context, options),
    OnVisualizationsUpdated);
```

```csharp
private void OnVisualizationsUpdated(HUDVisualizations visualizations,
    InputDownstreamContext context, FrameDrawOptions options)
{
    if (ReferenceEquals(ExtendedHud, visualizations))
    {
        return;
    }

    ExtendedHud = visualizations;

    try
    {
        visualizations.AddVisualization<MyVisualization>();
    }
    catch (Exception exception)
    {
        // The factory may refuse a type it cannot construct - fall back rather than die.
        Logger.Exception?.LogException(exception);
    }
}
```

Two reasons to hook the update rather than the constructor: the icon prefab and its parent
transform are certain to exist by then, and the HUD is rebuilt per session so you get a
fresh instance to compare against — track it by reference and you re-add correctly after
returning to the menu and loading again.

> [!NOTE]
> `AddVisualization<T>()` builds the instance with `Factory.Create<T>()`, the game's
> dependency container. Your constructor parameters must be types it can resolve —
> `Player`, `Viewport`, `DrawManager` and similar are fine. It cannot inject anything of
> yours, which is why the example passes the overlay through a static field.

## Icons

`GetIcon()` returns a `Sprite`. The simplest source is the game's own set, which keeps the
bar looking native:

```csharp
Globals.Resources.Icons.StatSpeed;                          // a throughput-ish icon
Globals.Resources.Icons.VisualizationIslandGrid;            // what the grid button uses
```

For your own art, load a PNG with `FileTextureLoader.LoadTextureAsSprite` — see
[load models and icons](load-models-and-icons.md).

## Keep a fallback while you are developing

The factory step is the one part of this that can fail in a way you cannot fully verify
without running the game. Catch it, log it, and leave a hotkey wired to the same toggle, so
a mod whose button never appeared is still usable. [Bind a key](keybind-toggle.md) covers
the hotkey side.

## See also

- [Draw in the world](draw-in-world.md) — what the toggle usually controls
- [Bind a key](keybind-toggle.md) — the fallback, and why there is no keybinding API
- [Notifications and HUD screens](notifications-and-hud-screens.md) — other ways to reach the player
