using System;
using Game.Core.Blueprint.Exporter;
using ILogger = Core.Logging.ILogger;

/// <summary>
/// Holds the session-scoped services this mod needs but cannot be handed directly.
///
/// The blueprint exporter and library are bound in the game session's dependency
/// container, so a reference to the orchestrator is enough to reach them. They are resolved
/// on first use rather than when the session is captured, which sidesteps any question of
/// whether the binding has happened yet.
/// </summary>
public class SessionServices
{
    private readonly ILogger Logger;

    private GameSessionOrchestrator Orchestrator;
    private IBlueprintExporter Exporter;
    private IBlueprintLibrary Library;

    public SessionServices(ILogger logger)
    {
        Logger = logger;
    }

    public void Capture(GameSessionOrchestrator orchestrator)
    {
        if (!ReferenceEquals(Orchestrator, orchestrator))
        {
            Orchestrator = orchestrator;
            Exporter = null;
            Library = null;
        }
    }

    public bool TryGetExporter(out IBlueprintExporter exporter)
    {
        Exporter = Exporter ?? Resolve<IBlueprintExporter>();
        exporter = Exporter;
        return exporter != null;
    }

    public bool TryGetLibrary(out IBlueprintLibrary library)
    {
        Library = Library ?? Resolve<IBlueprintLibrary>();
        library = Library;
        return library != null;
    }

    private T Resolve<T>() where T : class
    {
        if (Orchestrator == null)
        {
            return null;
        }

        try
        {
            return Orchestrator.DependencyContainer.Resolve<T>();
        }
        catch (Exception exception)
        {
            Logger.Exception?.LogException(exception);
            return null;
        }
    }
}
