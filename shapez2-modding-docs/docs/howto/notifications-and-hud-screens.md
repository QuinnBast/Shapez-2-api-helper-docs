# Show a notification or open a HUD screen

**Problem.** You want to tell the player something, or send them to the research screen,
without building any UI.

**Solution.** `HUDEvents` is a bag of public multicast events that the HUD listens to.
Firing one is the cheapest possible player-facing output.

## Getting hold of `HUDEvents`

There is no static accessor for the HUD. The reliable route is to capture the instance
as the HUD constructs itself — `HUDPart.Construct` is public and receives it:

```csharp
public void Construct(HUDEvents events, Player player, ILogger logger, IAnalyticsTracker analytics)
```

So postfix that and keep the reference:

```csharp
private HUDEvents Events;

public MyMod(ILogger logger)
{
    ConstructHook = DetourHelper.CreatePostfixHook<HUDPart, HUDEvents, Player, ILogger, IAnalyticsTracker>(
        (part, events, player, log, analytics) => part.Construct(events, player, log, analytics),
        (part, events, player, log, analytics) => Events = events);
}
```

This fires once per HUD part, which is many times — you are simply overwriting the same
reference with the same object, which is harmless. `Events` is null until the first HUD
part constructs, so null-check before using it.

## Show a notification

```csharp
Events?.ShowNotification.Invoke(new HUDNotificationData(
    HUDNotificationType.Info,
    new RawText("Overlay enabled")));
```

The full constructor:

```csharp
HUDNotificationData(
    HUDNotificationType type,
    IText text,
    Sprite overrideIcon = null,
    Action action = null,
    float showDuration = 5f)
```

- **`text`** — an `IText`. `new RawText("…")` for literal strings; `"key".T()` for
  anything a player will read in their own language
  ([translations](add-translations.md)).
- **`action`** — a callback invoked if the player clicks the notification. This is the
  cheapest interactive UI in the game: notify *and* offer a jump to the relevant thing.
- **`showDuration`** — seconds. Default 5.

Use `RawText` only for debugging output. Anything shipped should be a translation key.

## Open a HUD screen

The same event bag drives most of the game's screens:

```csharp
Events?.ShowResearch.Invoke();
Events?.ShowStatistics.Invoke();
Events?.ShowBlueprintLibrary.Invoke();
Events?.ShowWiki.Invoke();
Events?.ShowWikiEntry.Invoke(new WikiEntryId("…"));
Events?.ShowResearchShop.Invoke();
Events?.ShowResearchSideQuests.Invoke();
Events?.ShowResearchPlayerLevelAndFocusOn.Invoke(new PlayerLevelGoalId("…"));
Events?.RequestAddBlueprintToLibrary.Invoke(blueprint, slot);
```

`ShowPauseMenu`, `ShowPauseMenuSettings`, and `ShowPauseMenuAdditionalContent` are there
too. Browse <xref:Root.HUDEvents> in the API reference for the current full list — it is a flat class of public fields, so it reads as documentation.

Two patterns these enable without writing UI:

- **Notification with an action that opens a screen** — "3 platforms are starved" →
  click → statistics.
- **Deep-link into the wiki** for your own building, if you register a wiki entry.

## Registering instead of invoking

The same events can be *listened* to, which is how you react to the player opening
something:

```csharp
Events.ShowStatistics.Register(OnStatisticsOpened);
// …
Events.ShowStatistics.Unregister(OnStatisticsOpened);
```

`MultiRegisterEvent` supports many handlers, so registering does not displace the
game's own. Always unregister on dispose.

## Gotchas

- **Do not spam notifications.** They queue and stack on screen. Rate-limit anything
  driven by a per-tick condition, and prefer one summary notification over one per
  building.
- `Events` is `null` before the HUD exists — during mod construction and at the main
  menu. Null-check every call, as above.
- Hooking `HUDPart.Construct` is a broad hook on a hot-ish path. It runs once per HUD
  part at session start, not per frame, so the cost is fine — but keep the postfix body
  to an assignment.
- Dispose the hook in `IMod.Dispose()`.

> [!NOTE]
> The `HUDPart.Construct` capture is the cleanest route I could verify. If ShapezShifter
> later exposes a HUD accessor, prefer it — this is reaching into the game's DI, and it
> is exactly the kind of thing an update can move.
