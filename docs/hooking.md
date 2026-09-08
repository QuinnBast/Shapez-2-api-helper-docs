# Hooking the Game

> **You need this when** neither Flow nor an interceptor covers what you want, and you
> have to patch a method directly.


When the game exposes no extension point for what you want, you patch it. ShapezShifter
wraps [MonoMod](https://github.com/MonoMod/MonoMod) in `DetourHelper`, which expresses
the target method as a lambda instead of a reflection string — so a rename becomes a
compile error rather than a runtime crash.

## Postfix — run after

```csharp
using ShapezShifter.SharpDetour;
using MonoMod.RuntimeDetour;

Hook hook = DetourHelper.CreatePostfixHook<MapDrawer, FrameDrawOptionsNoLOD>(
    (drawer, options) => drawer.Draw(options),      // target, as a call expression
    (drawer, options) => Overlay.Draw(options));    // your code, same parameters
```

The first lambda is **never executed** — it is parsed as an expression tree to identify
the method. Write it as a normal call on the parameters.

Overloads exist for zero to seven arguments, for `void` and for value-returning
methods, and for static methods (`CreateStaticPrefixHook`):

```csharp
CreatePostfixHook<TObject>(Expression<Action<TObject>>, Action<TObject>)
CreatePostfixHook<TObject, TArg0>(Expression<Action<TObject, TArg0>>, Action<TObject, TArg0>)
CreatePostfixHook<TObject, TArg0, TArg1>(…)
// …up to TArg6
```

## Prefix — run before, and optionally rewrite the arguments

A prefix **returns** the arguments the original will receive. With one argument you
return the new value; with several you return a tuple:

```csharp
DetourHelper.CreatePrefixHook<SomeType, float>(
    (target, amount) => target.DoThing(amount),
    (target, amount) => amount * 2.0f);          // the original now sees double

DetourHelper.CreatePrefixHook<SomeType, int, string>(
    (target, count, label) => target.DoThing(count, label),
    (target, count, label) => (count + 1, label.ToUpper()));
```

For a `void`-target with no arguments the prefix is a plain `Action<TObject>` — no
return value to rewrite.

## Choosing a target

The method you hook is a contract you are inventing, so pick a stable one:

- **Prefer methods with meaningful signatures.** `MapDrawer.Draw(FrameDrawOptionsNoLOD)`
  hands you everything you need; a private helper with three primitive parameters tells
  you nothing and moves between versions.
- **Prefer things called once per frame or once per session** over things called per
  item. A hook on a hot path multiplies its cost by the size of the player's factory.
- **Hook the widest point that works.** One hook on the draw entry point beats twenty on
  individual drawers.
- **Avoid compiler-generated members** — lambdas, iterator state machines, local
  functions (`<>c__DisplayClass…`). Their names are not stable across builds.

## Interceptors and rewirers

Before writing a detour, check whether Hijack already covers the structure you want to
extend. The pattern is a pair: an **interceptor** that hooks the game's construction of
something, and a **rewirer** interface you implement to contribute to it.

```csharp
public interface ITickRewirer : IRewirer
{
    void Tick(float deltaTime);
}
```

Register with `GameRewirers.AddRewirer(...)`, which returns a disposable
`RewirerHandle`. The full list of interception points is in
[Architecture](architecture.md#hijack--shapezshifterhijack).

This is worth preferring over a raw detour whenever it fits: interceptors are part of
ShapezShifter's API surface, so they are maintained across game updates, whereas your
detour target is not.

Ticks have a one-line wrapper, which is all most mods need:

```csharp
using ShapezShifter.Flow;
RewirerHandle handle = this.OnTick(dt => Update(dt));
```

## Chaining, not replacing

Whenever you take over a delegate the game owns — a lane's `AcceptHook`, a `DrawHooks`
delegate, an event — save the previous value and call it:

```csharp
AcceptHookDelegate saved = lane.AcceptHook;
lane.AcceptHook = delegate(IItemReceiver receiver, ref IBeltItem item, ref Ticks remaining_T)
{
    Observe(item);
    saved?.Invoke(receiver, ref item, ref remaining_T);
};
```

Replacing outright is the most common way to break a machine while your own code looks
correct — the cutter that no longer cuts, the belt that no longer moves.

## Disposing

Every hook is disposable and every rewirer registration hands back a handle. Undo both
in `IMod.Dispose()`:

```csharp
public void Dispose()
{
    DrawHook?.Dispose();
    TickHandle?.Dispose();
}
```

## Staying compatible

The game's internals are not a public API, and the shipped assemblies carry no
compatibility promise. Practical mitigations:

- **Keep hooks few and central.** A mod with two hooks survives an update that a mod
  with twenty will not.
- **Isolate them.** One file that installs every hook, so a version bump is one file to
  review.
- **Never assume a hook installed.** Wrap installation in `try`/`catch`, log the
  failure, and let the rest of the mod carry on degraded rather than dying at load.
- **Watch for `[Obsolete]`.** The game marks types it intends to replace —
  `DrawHooks`, `IGameSessionManagers`, `IslandModel.LayoutQuery`. They work today, and
  they are exactly what will change tomorrow.
- **Declare your range** in `manifest.json` (`GameVersionSupportRange`) rather than
  letting the game load your mod into a version you have never tested.
