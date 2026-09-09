using System;
using JetBrains.Annotations;
using MonoMod.RuntimeDetour;
using ShapezShifter.Hijack;
using ShapezShifter.SharpDetour;
using ILogger = Core.Logging.ILogger;

/// <summary>
/// Mod Reloader — a development tool.
///
/// Rebuild a mod, run `mrl.reload &lt;name&gt;` in the console, and its new code runs without
/// restarting the game. On a large save that turns a two-minute reload into a second.
///
/// This is deliberately not something to ship alongside a mod. See <see cref="Reloader"/>
/// for why: every reload leaks an assembly, and a mod is only as reloadable as its
/// `Dispose` is thorough.
/// </summary>
[UsedImplicitly]
public class ModReloaderMod : IMod
{
    private readonly ILogger Logger;
    private readonly ModRegistry Registry;
    private readonly Hook SessionHook;
    private readonly RewirerHandle CommandsHandle;

    public ModReloaderMod(ILogger logger)
    {
        Logger = logger;
        Registry = new ModRegistry(logger);

        // Runs once per session and hands over the orchestrator, whose dependency container
        // holds the modding framework.
        SessionHook = DetourHelper.CreatePostfixHook<GameSessionOrchestrator, IslandsModulesLookup>(
            (orchestrator, lookup) => orchestrator.InjectIslandsModuleProviders(lookup),
            OnSessionReady);

        Seeder seeder = new Seeder(logger);

        CommandsHandle = GameRewirers.AddRewirer(
            new ReloaderCommands(logger, Registry, new Reloader(logger, Registry), seeder));

        // A staged build with nothing installed beside it is invisible to the game, so the
        // first build of a new mod would otherwise have nothing to reload. Installing it
        // here means the next launch loads it normally, checks included.
        foreach (string line in seeder.Seed())
        {
            Logger.Info?.Log(line);
        }

        Logger.Info?.Log("Mod Reloader ready - mrl.list, then mrl.reload <name> (F1).");
    }

    private void OnSessionReady(GameSessionOrchestrator orchestrator, IslandsModulesLookup lookup)
    {
        try
        {
            Registry.Capture(orchestrator);
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
