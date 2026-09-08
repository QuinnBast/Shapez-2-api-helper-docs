using System;
using JetBrains.Annotations;
using MonoMod.RuntimeDetour;
using ShapezShifter.Hijack;
using ShapezShifter.SharpDetour;
using ILogger = Core.Logging.ILogger;

/// <summary>
/// Platform Blackbox.
///
/// Groundwork for collapsing a selection of platforms into a single stand-in platform. What
/// works today is the part everything else depends on: reading a selection, working out
/// which of its ports cross the boundary, and capturing it as a blueprint that can be
/// placed again.
///
/// The boundary analysis is the interesting half. A selection is only replaceable by a
/// small platform if the number of ports leaving it is small - everything wired internally
/// is detail that a stand-in would hide.
/// </summary>
[UsedImplicitly]
public class PlatformBlackboxMod : IMod
{
    private readonly ILogger Logger;
    private readonly SessionServices Session;
    private readonly Hook SessionHook;
    private readonly RewirerHandle CommandsHandle;

    public PlatformBlackboxMod(ILogger logger)
    {
        Logger = logger;
        Session = new SessionServices(logger);

        // Runs once per session and hands over the orchestrator, which owns the dependency
        // container the blueprint exporter and library are bound into.
        SessionHook = DetourHelper.CreatePostfixHook<GameSessionOrchestrator, IslandsModulesLookup>(
            (orchestrator, lookup) => orchestrator.InjectIslandsModuleProviders(lookup),
            OnSessionReady);

        CommandsHandle = GameRewirers.AddRewirer(new BlackboxCommands(logger, Session));

        Logger.Info?.Log("Platform Blackbox ready - select platforms and run pbx.analyze (F1).");
    }

    private void OnSessionReady(GameSessionOrchestrator orchestrator, IslandsModulesLookup lookup)
    {
        try
        {
            Session.Capture(orchestrator);
        }
        catch (Exception exception)
        {
            Logger.Exception?.LogException(exception);
        }
    }

    public void Dispose()
    {
        GameRewirers.RemoveRewirer(CommandsHandle);
        SessionHook?.Dispose();
    }
}
