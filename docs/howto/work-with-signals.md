# Work with signals and wires

**Problem.** You want to read a wire's value, or make your building respond to a signal.

Signals are the game's logic layer: wires carry an `ISignal`, and a connected run of
wires is a network that recomputes every tick.

## Signal values

```csharp
public interface ISignal { }
```

Deliberately open — concrete signal types carry shapes, colours, booleans, or nothing.
The empty case is a singleton:

```csharp
NullSignal.Instance
```

`LastOutput` on a network is `NullSignal.Instance` rather than `null` when there is no
signal, so **check against `NullSignal`, not null**:

```csharp
if (simulation.LastOutput is NullSignal)
{
    // no signal on the wire
}
```

## Reading a wire network

```csharp
public class SignalNetworkSimulation : ISignalSimulation, IUpdatableSimulation
{
    public ISignal LastOutput { get; }

    public int NumSignalProviders { get; }
    public int NumSignalReceivers { get; }
    public int NumSignalConductors { get; }

    public ISignalProvider GetSignalProvider(int index);
    public ISignalReceiver GetSignalReceiver(int index);
}
```

Getting there is the usual simulator lookup:

```csharp
if (map.Simulator.TryFindTileSimulation(tile_G, out ILocalizedTileSimulation localized)
    && localized.Simulation is SignalNetworkSimulation signalNetwork)
{
    ISignal current = signalNetwork.LastOutput;
}
```

`LastOutput` is the computed value for the network as of the last tick — read it, do not
try to recompute it.

## Providers and receivers

The mental model matches fluids: a network has things that **provide** a signal and
things that **receive** it.

```csharp
ISignalProvider          // produces a signal
ISignalReceiver          // consumes one
ISignalProviderConnector // the connector-side interfaces
ISignalReceiverConnector
```

A network with receivers but no providers reads `NullSignal` — the wiring equivalent of
a starved machine.

## Signal channels

Wireless signals go through channels rather than wires:

```csharp
ISignalChannelRegistry
ISignalChannel
```

`SignalChannelRegistry` is a field on `GameSessionOrchestrator`, and
`SignalChannelIdGenerator` mints ids. This is the mechanism behind the game's
transmitter/receiver buildings, so a mod that wants to publish or consume a wireless
signal works through the channel registry rather than touching wires.

## Giving a building signal ports

Declared on the connector data, like shape and fluid IO:

```csharp
BuildingSignalInput      // reads a signal
BuildingSignalOutput     // writes one
BuildingSignalJunction   // passes through
```

Each derives from `BuildingSignalIO` and carries a `BuildingSignalIOType`
(`Building` by default) plus `IsCompatibleConnection(BuildingBaseIO)`, which is what
decides whether the player can wire two things together.

```csharp
IBuildingConnectorData connectors = BuildingConnectors.SingleTile()
   .AddSignalInput(/* signal connector config */)
   .AddSignalOutput(/* … */)
   .Build();
```

See [Add a building](add-a-building.md#the-building).

## Vanilla behaviours worth copying

Rather than inventing signal handling, read these:

| Class | What it does |
| --- | --- |
| `ConstantSignalBuildingModuleDataProvider` | the constant-signal building's panel — how a player-set signal is configured |
| `ControlledSignalTransmitterBuildingModuleDataProvider` | publishing to a channel |
| `ControlledSignalReceiverBuildingModuleDataProvider` | subscribing to a channel |
| `SignalControlledDistributionBehaviour` | a machine whose behaviour is driven by a signal — the closest template for "my building reacts to a wire" |
| `DisplayBuildingModuleDataProvider` | rendering a signal's value to the player |

`SignalControlledDistributionBehaviour` and its `…State` are the ones to study if your
building should change what it does based on an incoming signal — it is the pattern the
game already ships.

## Signal ports between platforms

Like belts and fluids, signals cross platform edges through ports, with their own
blocked states:

```csharp
SignalPortTransferSimulation
SignalPortBlockedSimulation
SignalPortSenderBlockedState
SignalPortReceiverDisabledSimulation
SignalPortSystem
```

`SignalPortReceiverDisabledSimulation` is worth knowing about: a *disabled* receiver is
a distinct state from a blocked one, and a signal that mysteriously fails to cross a
platform boundary is usually this.

## Gotchas

- **`NullSignal.Instance`, not `null`.** Null-checking a signal will not detect "no
  signal".
- Signal networks update on the simulation tick. Reading `LastOutput` mid-frame gives
  you last tick's value, which is correct — do not try to force a recompute.
- `ISignal` is an empty interface, so you must pattern-match to concrete types to read a
  value. Handle the unknown case; another mod may introduce signal types you have never
  seen.
- Do not write to a network you do not own. Provide a signal through a provider
  connector on your own building instead.

> [!NOTE]
> No sample mod adds a signal-driven building, so this page is assembled from the
> vanilla implementations rather than from a worked example. Verify the connector
> configuration against your game version.
